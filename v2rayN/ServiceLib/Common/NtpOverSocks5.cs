using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ServiceLib.Common;
public class NtpOverSocks5 : IDisposable
{
    private static readonly string _tag = "NtpOverSocks5";

    private readonly string _socks5Host;
    private readonly int _socks5TcpPort;

    private TcpClient? _tcpControlClient;
    private NetworkStream? _tcpControlStream;
    private UdpClient? _localUdpClient;
    private IPEndPoint? _proxyRelayDestEp;
    private IPEndPoint? _clientListenEp;

    private const byte Socks5Version = 0x05;
    private const byte SocksCmdUdpAssociate = 0x03;
    private const int NtpDefaultPort = 123;

    #region SOCKS5 Address Handling
    private class Socks5AddressData
    {
        public const byte AddrTypeIPv4 = 0x01;
        public const byte AddrTypeDomain = 0x03;
        public const byte AddrTypeIPv6 = 0x04;

        public byte AddressType { get; set; }
        public string Host { get; set; } = string.Empty;
        public ushort Port { get; set; }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            ms.WriteByte(AddressType);
            switch (AddressType)
            {
                case AddrTypeIPv4:
                    if (IPAddress.TryParse(Host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ms.Write(ip.GetAddressBytes(), 0, 4);
                    }
                    else
                    {
                        ms.Write(new byte[] { 0, 0, 0, 0 });
                        Logging.SaveLog(_tag + $": S5AddrData Warning: Could not parse '{Host}' as IPv4, using 0.0.0.0.");
                    }
                    break;
                case AddrTypeDomain:
                    if (string.IsNullOrEmpty(Host))
                    {
                        ms.WriteByte(0);
                    }
                    else
                    {
                        var domainBytes = Encoding.ASCII.GetBytes(Host);
                        ms.WriteByte((byte)domainBytes.Length);
                        ms.Write(domainBytes);
                    }
                    break;
                case AddrTypeIPv6:
                    if (IPAddress.TryParse(Host, out var ip6) && ip6.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ms.Write(ip6.GetAddressBytes(), 0, 16);
                    }
                    else
                    {
                        ms.Write(new byte[16]);
                        Logging.SaveLog(_tag + $": S5AddrData Warning: Could not parse '{Host}' as IPv6, using ::.");
                    }
                    break;
                default:
                    Logging.SaveLog(_tag + $": S5AddrData Error: Address type {AddressType} not supported for ToBytes().");
                    throw new NotSupportedException($"SOCKS5 address type {AddressType} not supported.");
            }
            var portBytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(portBytes, Port);
            ms.Write(portBytes);
            return ms.ToArray();
        }

        public static async Task<Socks5AddressData?> ParseAsync(Stream stream, CancellationToken ct)
        {
            var addr = new Socks5AddressData();
            var typeByte = new byte[1];
            try
            {
                if (await stream.ReadAsync(typeByte.AsMemory(0, 1), ct).ConfigureAwait(false) < 1)
                    return null;
                addr.AddressType = typeByte[0];
                switch (addr.AddressType)
                {
                    case AddrTypeIPv4:
                        var ipv4Bytes = new byte[4];
                        if (await stream.ReadAsync(ipv4Bytes.AsMemory(0, 4), ct).ConfigureAwait(false) < 4)
                        {
                            return null;
                        }
                        addr.Host = new IPAddress(ipv4Bytes).ToString();
                        break;
                    case AddrTypeDomain:
                        var lenByte = new byte[1];
                        if (await stream.ReadAsync(lenByte.AsMemory(0, 1), ct).ConfigureAwait(false) < 1)
                        {
                            return null;
                        }
                        if (lenByte[0] == 0)
                        {
                            addr.Host = string.Empty;
                        }
                        else
                        {
                            var domainBytes = new byte[lenByte[0]];
                            if (await stream.ReadAsync(domainBytes.AsMemory(0, domainBytes.Length), ct).ConfigureAwait(false) < domainBytes.Length)
                            {
                                return null;
                            }
                            addr.Host = Encoding.ASCII.GetString(domainBytes);
                        }
                        break;
                    case AddrTypeIPv6:
                        byte[] ipv6Bytes = new byte[16];
                        if (await stream.ReadAsync(ipv6Bytes.AsMemory(0, 16), ct).ConfigureAwait(false) < 16)
                            return null;
                        addr.Host = new IPAddress(ipv6Bytes).ToString();
                        break;
                    default:
                        Logging.SaveLog(_tag + $": S5AddrData Unsupported ATYP: {addr.AddressType}");
                        return null;
                }
                var portBytes = new byte[2];
                if (await stream.ReadAsync(portBytes.AsMemory(0, 2), ct).ConfigureAwait(false) < 2)
                    return null;
                addr.Port = BinaryPrimitives.ReadUInt16BigEndian(portBytes);
                return addr;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Logging.SaveLog(_tag + ": S5AddrData Stream error during ParseAsync", ex);
                return null;
            }
        }
    }
    #endregion

    #region NTP Packet Handling
    private static byte[] BuildNtpClientRequestPacket()
    {
        byte[] ntpReq = new byte[48];
        ntpReq[0] = 0x23; // LI=0, VN=4, Mode=3
        return ntpReq;
    }

    private static bool TryParseNtpServerResponse(byte[] ntpResponseBytes, out DateTime serverTransmitTimeUtc, out string? parseStatusMessage)
    {
        serverTransmitTimeUtc = DateTime.MinValue;
        parseStatusMessage = null;
        if (ntpResponseBytes == null || ntpResponseBytes.Length < 48)
        {
            parseStatusMessage = "NTP response too short.";
            return false;
        }
        if ((ntpResponseBytes[0] & 0x07) != 4)
        {
            parseStatusMessage = "Not NTP server mode packet.";
            return false;
        }
        try
        {
            var secsSince1900 = BinaryPrimitives.ReadUInt32BigEndian(ntpResponseBytes.AsSpan(40, 4));
            const long ntpToUnixEpochOffsetSeconds = 2208988800L;
            var unixSecs = (long)secsSince1900 - ntpToUnixEpochOffsetSeconds;
            serverTransmitTimeUtc = DateTimeOffset.FromUnixTimeSeconds(unixSecs).UtcDateTime;
            return true;
        }
        catch (Exception ex)
        {
            parseStatusMessage = $"Timestamp parse ex: {ex.GetType().Name}";
            return false;
        }
    }
    #endregion

    #region Constructors and Public Interface
    public NtpOverSocks5(string socks5Host, int socks5TcpPort)
    {
        _socks5Host = socks5Host;
        _socks5TcpPort = socks5TcpPort;
    }

    public async Task<NtpViaProxyResult> GetNtpTimeAsync(string targetNtpServerHost, TimeSpan operationTimeout, bool useDomainDirectly = true, int retryCount = 2)
    {
        var attempts = new List<NtpViaProxyResult>();
        var totalTimeout = operationTimeout;
        var perAttemptTimeout = TimeSpan.FromMilliseconds(totalTimeout.TotalMilliseconds / retryCount);

        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            var result = await GetNtpTimeInternalAsync(targetNtpServerHost, perAttemptTimeout, useDomainDirectly);
            attempts.Add(result);

            //if (result.Success)
            //{
            //    if (attempt == 0)
            //    {
            //        return result;
            //    }
            //    break;
            //}

            if (attempt < retryCount - 1)
            {
                await Task.Delay(100);
            }
        }

        var successfulAttempts = attempts.Where(r => r.Success).ToList();
        
        if (successfulAttempts.Count != 0)
        {
            var bestResult = successfulAttempts.OrderBy(r => r.RoundTripTime?.TotalMilliseconds ?? double.MaxValue).First();
            return new NtpViaProxyResult
            {
                Success = true,
                RoundTripTime = bestResult.RoundTripTime,
                ServerTimeUtc = bestResult.ServerTimeUtc,
                StatusMessage = bestResult.StatusMessage
            };
        }

        return attempts.Last();
    }

    private async Task<NtpViaProxyResult> GetNtpTimeInternalAsync(string targetNtpServerHost, TimeSpan operationTimeout, bool useDomainDirectly = true)
    {
        var result = new NtpViaProxyResult();
        var stopwatch = new Stopwatch();
        using var cts = new CancellationTokenSource(operationTimeout);
        var cancellationToken = cts.Token;

        try
        {
            if (!await EstablishUdpAssociationAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Success = false;
                result.StatusMessage = "SOCKS5 UDP Association Failed";
                result.ErrorDetail = "Could not complete SOCKS5 handshake or UDP ASSOCIATE command.";
                return result;
            }

            if (_localUdpClient == null || _proxyRelayDestEp == null)
            {
                result.Success = false;
                result.StatusMessage = "Internal Error";
                result.ErrorDetail = "UDP relay client not properly initialized after association.";
                return result;
            }

            IPAddress? targetNtpIp = null;
            if (!useDomainDirectly)
            {
                try
                {
                    if (!IPAddress.TryParse(targetNtpServerHost, out targetNtpIp!))
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(targetNtpServerHost).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                        targetNtpIp = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ??
                                       hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetworkV6) ??
                                       hostEntry.AddressList.FirstOrDefault() ??
                                       throw new ArgumentException($"Could not resolve '{targetNtpServerHost}' to an IP address.");
                    }
                }
                catch (Exception dnsEx)
                {
                    result.Success = false;
                    result.StatusMessage = "NTP Target DNS Error";
                    result.ErrorDetail = $"Failed to resolve NTP server '{targetNtpServerHost}': {dnsEx.Message}";
                    return result;
                }
            }

            var ntpRequestPayload = BuildNtpClientRequestPacket();
            using var socksUdpReqMs = new MemoryStream();
            socksUdpReqMs.Write(new byte[] { 0x00, 0x00, 0x00 });
            var targetSocksAddr = new Socks5AddressData
            {
                AddressType = useDomainDirectly ? Socks5AddressData.AddrTypeDomain : (targetNtpIp!.AddressFamily == AddressFamily.InterNetwork ? Socks5AddressData.AddrTypeIPv4 : Socks5AddressData.AddrTypeIPv6),
                Host = useDomainDirectly ? targetNtpServerHost : targetNtpIp.ToString(),
                Port = (ushort)NtpDefaultPort
            };
            socksUdpReqMs.Write(targetSocksAddr.ToBytes());
            socksUdpReqMs.Write(ntpRequestPayload);
            var fullUdpPacket = socksUdpReqMs.ToArray();

            stopwatch.Start();
            await _localUdpClient.SendAsync(fullUdpPacket, _proxyRelayDestEp!, cancellationToken).ConfigureAwait(false);
            var udpReceiveResult = await _localUdpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            result.RoundTripTime = stopwatch.Elapsed;
            var receivedData = udpReceiveResult.Buffer;

            if (receivedData.Length < 4 + 1 + 4 + 2)
            {
                result.Success = false;
                result.StatusMessage = "SOCKS5 UDP Response Too Short";
                result.ErrorDetail = "The received UDP packet via SOCKS5 was too short to contain a valid SOCKS5 UDP header.";
                return result;
            }

            using var responseStream = new MemoryStream(receivedData);
            responseStream.Seek(3, SeekOrigin.Begin);

            var originalSrcSocksAddr = await Socks5AddressData.ParseAsync(responseStream, cancellationToken).ConfigureAwait(false);

            var actualNtpPayload = new byte[responseStream.Length - responseStream.Position];
            await responseStream.ReadAsync(actualNtpPayload, 0, actualNtpPayload.Length, cancellationToken).ConfigureAwait(false);

            if (TryParseNtpServerResponse(actualNtpPayload, out var serverTimeUtc, out var ntpParseError))
            {
                result.Success = true;
                result.ServerTimeUtc = serverTimeUtc;
                result.StatusMessage = ResUI.SpeedtestingCompleted;
            }
            else
            {
                result.Success = false;
                result.StatusMessage = ResUI.NtpError;
                result.ErrorDetail = ntpParseError ?? "Failed to parse NTP packet content.";
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.StatusMessage = ResUI.NtpTimeout;
            result.ErrorDetail = "The NTP operation via SOCKS5 timed out.";
        }
        catch (SocketException se)
        {
            result.Success = false;
            result.StatusMessage = "SOCKS5 Network Error";
            result.ErrorDetail = $"SocketException (code {se.SocketErrorCode}): {se.Message}";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.StatusMessage = "NTP Test General Error";
            result.ErrorDetail = $"{ex.GetType().Name}: {ex.Message}";
            Logging.SaveLog(_tag + ": GetNtpTimeInternalAsync error", ex);
        }
        finally
        {
            Dispose();
        }
        return result;
    }

    public void Dispose()
    {
        _tcpControlStream?.Dispose();
        _tcpControlClient?.Close();
        _localUdpClient?.Close();
        _tcpControlStream = null;
        _tcpControlClient = null;
        _localUdpClient = null;
    }
    #endregion

    #region SOCKS5 Connection Handling
    private async Task<bool> EstablishUdpAssociationAsync(CancellationToken cancellationToken)
    {
        _localUdpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        _clientListenEp = (IPEndPoint)_localUdpClient.Client.LocalEndPoint!;

        _tcpControlClient = new TcpClient();
        try
        {
            await _tcpControlClient.ConnectAsync(_socks5Host, _socks5TcpPort, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return false;
        }
        _tcpControlStream = _tcpControlClient.GetStream();

        byte[] handshakeRequest = { Socks5Version, 0x01, 0x00 };
        await _tcpControlStream.WriteAsync(handshakeRequest, cancellationToken).ConfigureAwait(false);
        var handshakeResponse = new byte[2];
        if (await _tcpControlStream.ReadAsync(handshakeResponse, cancellationToken).ConfigureAwait(false) < 2 ||
            handshakeResponse[0] != Socks5Version || handshakeResponse[1] != 0x00)
        {
            return false;
        }

        var clientListenIp = _clientListenEp.Address;
        var clientAddrForSocks = new Socks5AddressData();
        if (clientListenIp.Equals(IPAddress.Any))
        {
            clientAddrForSocks.AddressType = Socks5AddressData.AddrTypeIPv4;
            clientAddrForSocks.Host = "0.0.0.0";
        }
        else if (clientListenIp.Equals(IPAddress.IPv6Any))
        {
            clientAddrForSocks.AddressType = Socks5AddressData.AddrTypeIPv6;
            clientAddrForSocks.Host = "::";
        }
        else if (clientListenIp.IsIPv4MappedToIPv6)
        {
            clientAddrForSocks.AddressType = Socks5AddressData.AddrTypeIPv4;
            clientAddrForSocks.Host = clientListenIp.MapToIPv4().ToString();
        }
        else if (clientListenIp.AddressFamily == AddressFamily.InterNetwork)
        {
            clientAddrForSocks.AddressType = Socks5AddressData.AddrTypeIPv4;
            clientAddrForSocks.Host = clientListenIp.ToString();
        }
        else if (clientListenIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            clientAddrForSocks.AddressType = Socks5AddressData.AddrTypeIPv6;
            clientAddrForSocks.Host = clientListenIp.ToString();
        }
        else
        {
            return false;
        }
        clientAddrForSocks.Port = (ushort)_clientListenEp.Port;

        using var udpAssociateReqMs = new MemoryStream();
        udpAssociateReqMs.WriteByte(Socks5Version);
        udpAssociateReqMs.WriteByte(SocksCmdUdpAssociate);
        udpAssociateReqMs.WriteByte(0x00);
        udpAssociateReqMs.Write(clientAddrForSocks.ToBytes());
        await _tcpControlStream.WriteAsync(udpAssociateReqMs.ToArray(), cancellationToken).ConfigureAwait(false);

        var verRepRsv = new byte[3];
        if (await _tcpControlStream.ReadAsync(verRepRsv, cancellationToken).ConfigureAwait(false) < 3 ||
            verRepRsv[0] != Socks5Version || verRepRsv[1] != 0x00)
        {
            return false;
        }

        var proxyRelaySocksAddr = await Socks5AddressData.ParseAsync(_tcpControlStream, cancellationToken).ConfigureAwait(false);
        if (proxyRelaySocksAddr == null || !IPAddress.TryParse(proxyRelaySocksAddr.Host, out var proxyRelayIp))
        {
            return false;
        }

        _proxyRelayDestEp = new IPEndPoint(proxyRelayIp, proxyRelaySocksAddr.Port);
        return true;
    }
    #endregion
}
