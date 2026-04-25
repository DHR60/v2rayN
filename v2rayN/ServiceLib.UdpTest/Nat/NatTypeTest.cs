namespace ServiceLib.UdpTest.Nat;

public class NatTypeTest
{
    private const int StunDefaultPort = 3478;
    private static readonly TimeSpan PerReceiveTimeout = TimeSpan.FromSeconds(8);
    private const ushort MappedAddressAttributeType = 0x0001;
    private const ushort XorMappedAddressAttributeType = 0x0020;
    private const ushort OtherAddressAttributeType = 0x802C;
    //private const ushort ChangeRequestAttributeType = 0x0003;
    private static readonly byte[] MagicCookie = [0x21, 0x12, 0xA4, 0x42];

    // STUN Message Type and Response Constants
    private const byte BindingRequestMessageType0 = 0x00;
    private const byte BindingRequestMessageType1 = 0x01;
    private const byte BindingSuccessResponseType0 = 0x01;
    private const byte BindingSuccessResponseType1 = 0x01;

    private string _ip1 = string.Empty;
    private int _ip1Port1 = -1;
    private string _ip2 = string.Empty;
    private int _ip2Port1 = -1;
    private int _ip2Port2 = -1;

    private string _mappedEndpoint1 = string.Empty;
    private string _mappedEndpoint2 = string.Empty;
    private string _mappedEndpoint3 = string.Empty;

    public StunResult Result { get; private set; } = new()
    {
        Socks5UdpChannelCreated = false,
        BindingSuccess = false,
        FilteringBehavior = SubResult.Failed,
        MappingBehavior = SubResult.Failed,
        MappedAddress = null,
    };

    public async Task StartTestAsync(string targetServerHost, int socks5Port, TimeSpan operationTimeout)
    {
        // Reset state
        ResetTestState();

        using var cts = new CancellationTokenSource(operationTimeout);
        var cancellationToken = cts.Token;
        // Get the target IP
        var (targetHost, targetPort) = ParseHostAndPort(targetServerHost);
        if (!IsDomain(targetHost))
        {
            _ip1 = targetHost;
        }
        else
        {
            var ips = await Dns.GetHostAddressesAsync(targetHost, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
            if (ips.Length <= 0)
            {
                throw new Exception("Failed to resolve target host to IP.");
            }
            _ip1 = ips[0].ToString();
            if (ips.Length > 1)
            {
                _ip2 = ips[1].ToString();
            }
        }
        _ip1Port1 = targetPort;
        _ip2Port1 = targetPort;
        // Establish UDP association with SOCKS5 proxy
        using var channel = new Socks5UdpChannel("127.0.0.1", socks5Port);
        if (!await channel.EstablishUdpAssociationAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new Exception("Failed to establish UDP association with SOCKS5 proxy.");
        }
        Result = Result with
        {
            Socks5UdpChannelCreated = true,
        };
        // Run STUN binding test
        await RunBindingTestAsync(channel, cancellationToken).ConfigureAwait(false);
        if (!Result.BindingSuccess)
        {
            return;
        }
        // Run STUN filtering behavior test
        await RunFilteringTestAsync(channel, cancellationToken).ConfigureAwait(false);
        // Run STUN mapping behavior test
        await RunMappingTestAsync(channel, cancellationToken).ConfigureAwait(false);
    }

    private void ResetTestState()
    {
        _ip1 = string.Empty;
        _ip1Port1 = -1;
        _ip2 = string.Empty;
        _ip2Port1 = -1;
        _ip2Port2 = -1;
        _mappedEndpoint1 = string.Empty;
        _mappedEndpoint2 = string.Empty;
        _mappedEndpoint3 = string.Empty;
        Result = new StunResult
        {
            Socks5UdpChannelCreated = false,
            BindingSuccess = false,
            FilteringBehavior = SubResult.Failed,
            MappingBehavior = SubResult.Failed,
            MappedAddress = null,
        };
    }

    private async Task RunBindingTestAsync(Socks5UdpChannel channel, CancellationToken cancellationToken)
    {
        // Build STUN binding request
        var transactionId = GenerateStunTransactionId();
        var data = BuildStunBindingRequest(transactionId);

        byte[] receiveResult;

        try
        {
            // Send the STUN request to the target server via the SOCKS5 UDP channel
            await channel.SendAsync(_ip1, (ushort)_ip1Port1, data).ConfigureAwait(false);

            // Receive the response
            (_, receiveResult) = await ReceiveWithTimeoutAsync(channel, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Result = Result with { Socks5UdpChannelCreated = true, BindingSuccess = false };
            return;
        }

        // Check if we got a valid STUN response
        if (!IsValidStunResponse(receiveResult, transactionId))
        {
            Result = Result with
            {
                BindingSuccess = false,
            };
            return;
        }

        // Get XOR-MAPPED-ADDRESS, MAPPED-ADDRESS attribute
        // and try get OTHER-ADDRESS
        ParseAddressAttributes(receiveResult, transactionId, out var xorMappedAddress, out var mappedAddress, out var otherAddress);

        var finalMappedAddress = xorMappedAddress ?? mappedAddress;
        if (string.IsNullOrEmpty(finalMappedAddress))
        {
            Result = Result with
            {
                BindingSuccess = false,
            };
            return;
        }
        Result = Result with
        {
            BindingSuccess = true,
            MappedAddress = finalMappedAddress,
        };
        _mappedEndpoint1 = finalMappedAddress;

        if (TryParseEndpoint(otherAddress, out var otherIp, out var otherPort))
        {
            if (!string.IsNullOrEmpty(otherIp))
            {
                _ip2 = otherIp;
            }
            if (otherPort is > 0 and <= 65535)
            {
                _ip2Port1 = otherPort;
            }
        }
    }

    private async Task RunFilteringTestAsync(Socks5UdpChannel channel, CancellationToken cancellationToken)
    {
        // Build STUN binding request
        var transactionId = GenerateStunTransactionId();

        // Test1: Send to ip1:port1, receive from ip2:port2
        var data1 = BuildStunRequestWithChangeRequest(transactionId, 0x06); // Change both IP and port

        var test1Success = false;
        byte[] receiveResult1 = null;
        Socks5UdpChannel.Socks5RemoteEndpoint? remoteEndpoint1 = null;

        try
        {
            await channel.SendAsync(_ip1, (ushort)_ip1Port1, data1).ConfigureAwait(false);
            (remoteEndpoint1, receiveResult1) = await ReceiveWithTimeoutAsync(channel, cancellationToken).ConfigureAwait(false);
            test1Success = true;
        }
        catch (TimeoutException)
        {
            test1Success = false;
        }

        if (remoteEndpoint1?.Host == _ip1)
        {
            Result = Result with { FilteringBehavior = SubResult.Unsupported };
            return;
        }

        // Check if we got a valid STUN response
        if (test1Success && IsValidStunResponse(receiveResult1, transactionId))
        {
            Result = Result with { FilteringBehavior = SubResult.EndpointIndependent };
            return;
        }

        // Test2: Send to ip1:port1, receive from ip1:port2
        var data2 = BuildStunRequestWithChangeRequest(transactionId, 0x02); // Change port only

        var test2Success = false;
        byte[] receiveResult2 = null;
        Socks5UdpChannel.Socks5RemoteEndpoint? remoteEndpoint2 = null;

        try
        {
            await channel.SendAsync(_ip1, (ushort)_ip1Port1, data2).ConfigureAwait(false);
            (remoteEndpoint2, receiveResult2) = await ReceiveWithTimeoutAsync(channel, cancellationToken).ConfigureAwait(false);
            test2Success = true;
        }
        catch (TimeoutException)
        {
            test2Success = false;
        }

        if (remoteEndpoint2 != null && (remoteEndpoint2.Host != _ip1 || remoteEndpoint2.Port == _ip1Port1))
        {
            Result = Result with { FilteringBehavior = SubResult.Unsupported };
            return;
        }

        // Check if we got a valid STUN response
        if (test2Success && IsValidStunResponse(receiveResult2, transactionId))
        {
            Result = Result with { FilteringBehavior = SubResult.AddressDependent };
            return;
        }

        // If both tests failed, it's address and port dependent filtering
        Result = Result with { FilteringBehavior = SubResult.AddressAndPortDependent };
    }

    private async Task RunMappingTestAsync(Socks5UdpChannel channel, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_ip2) || _ip2Port1 <= 0)
        {
            Result = Result with { MappingBehavior = SubResult.Unsupported };
            return;
        }

        var transactionId = GenerateStunTransactionId();
        var data = BuildStunBindingRequest(transactionId);

        // Test1: Send to ip2:port1
        var test1Success = false;
        byte[] receiveResult1 = null;

        try
        {
            await channel.SendAsync(_ip2, (ushort)_ip2Port1, data).ConfigureAwait(false);
            (_, receiveResult1) = await ReceiveWithTimeoutAsync(channel, cancellationToken).ConfigureAwait(false);
            test1Success = true;
        }
        catch (TimeoutException)
        {
            test1Success = false;
        }

        if (test1Success && IsValidStunResponse(receiveResult1, transactionId))
        {
            ParseAddressAttributes(receiveResult1, transactionId, out var xorMappedAddress, out var mappedAddress, out var otherAddress);
            var finalMappedAddress = xorMappedAddress ?? mappedAddress;
            if (!string.IsNullOrEmpty(finalMappedAddress))
            {
                _mappedEndpoint2 = finalMappedAddress;
                if (_mappedEndpoint1 == _mappedEndpoint2)
                {
                    Result = Result with { MappingBehavior = SubResult.EndpointIndependent };
                    return;
                }

                // Get the port from OTHER-ADDRESS attribute, if available
                if (TryParseEndpoint(otherAddress, out var otherIp, out var otherPort))
                {
                    if (otherIp == _ip2 && otherPort is > 0 and <= 65535)
                    {
                        _ip2Port2 = otherPort;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(_mappedEndpoint2))
        {
            Result = Result with { MappingBehavior = SubResult.Failed };
            return;
        }

        if (_ip2Port2 is <= 0 or > 65535)
        {
            Result = Result with { MappingBehavior = SubResult.Unsupported };
            return;
        }

        // Test2: Send to ip2:port2
        var test2Success = false;
        byte[] receiveResult2 = null;

        try
        {
            await channel.SendAsync(_ip2, (ushort)_ip2Port2, data).ConfigureAwait(false);
            (_, receiveResult2) = await ReceiveWithTimeoutAsync(channel, cancellationToken).ConfigureAwait(false);
            test2Success = true;
        }
        catch (TimeoutException)
        {
            test2Success = false;
        }

        if (test2Success && IsValidStunResponse(receiveResult2, transactionId))
        {
            ParseAddressAttributes(receiveResult2, transactionId, out var xorMappedAddress, out var mappedAddress, out _);
            var finalMappedAddress = xorMappedAddress ?? mappedAddress;
            if (!string.IsNullOrEmpty(finalMappedAddress))
            {
                _mappedEndpoint3 = finalMappedAddress;
                if (_mappedEndpoint1 == _mappedEndpoint3)
                {
                    Result = Result with { MappingBehavior = SubResult.AddressDependent };
                    return;
                }
            }
        }

        // If test passed but addresses don't match, it's address and port dependent
        Result = Result with { MappingBehavior = SubResult.AddressAndPortDependent };
    }

    private static void ParseAddressAttributes(byte[] stunMessage, byte[] transactionId, out string? xorMappedAddress, out string? mappedAddress, out string? otherAddress)
    {
        xorMappedAddress = null;
        mappedAddress = null;
        otherAddress = null;

        if (stunMessage.Length < 20)
        {
            return;
        }

        var messageLength = ReadUInt16(stunMessage, 2);
        var attributesEnd = Math.Min(stunMessage.Length, 20 + messageLength);
        var offset = 20;

        while (offset + 4 <= attributesEnd)
        {
            var attributeType = ReadUInt16(stunMessage, offset);
            var attributeLength = ReadUInt16(stunMessage, offset + 2);
            var valueOffset = offset + 4;

            if (valueOffset + attributeLength > attributesEnd)
            {
                break;
            }

            var attributeValue = stunMessage.AsSpan(valueOffset, attributeLength);

            if (attributeType == XorMappedAddressAttributeType)
            {
                if (TryParseAddressAttribute(attributeValue, true, transactionId, out var xorAddress))
                {
                    xorMappedAddress ??= xorAddress;
                }
            }
            else if (attributeType == MappedAddressAttributeType)
            {
                if (TryParseAddressAttribute(attributeValue, false, transactionId, out var mapped))
                {
                    mappedAddress ??= mapped;
                }
            }
            else if (attributeType == OtherAddressAttributeType)
            {
                if (TryParseAddressAttribute(attributeValue, false, transactionId, out var other))
                {
                    otherAddress ??= other;
                }
            }

            offset = valueOffset + attributeLength;
            var padding = (4 - (attributeLength % 4)) % 4;
            offset += padding;
        }
    }

    private static bool TryParseAddressAttribute(ReadOnlySpan<byte> attributeValue, bool xorAddress, ReadOnlySpan<byte> transactionId, out string? endpoint)
    {
        endpoint = null;

        if (attributeValue.Length < 4)
        {
            return false;
        }

        var family = attributeValue[1];
        var port = (ushort)((attributeValue[2] << 8) | attributeValue[3]);

        if (xorAddress)
        {
            port ^= (ushort)((MagicCookie[0] << 8) | MagicCookie[1]);
        }

        if (family == 0x01) // IPv4
        {
            if (attributeValue.Length < 8)
            {
                return false;
            }

            Span<byte> ipBytes = stackalloc byte[4];
            attributeValue.Slice(4, 4).CopyTo(ipBytes);

            if (xorAddress)
            {
                XorBytes(ipBytes, MagicCookie, 0);
            }

            endpoint = $"{new IPAddress(ipBytes)}:{port}";
            return true;
        }

        if (family == 0x02) // IPv6
        {
            if (attributeValue.Length < 20 || transactionId.Length < 12)
            {
                return false;
            }

            Span<byte> ipBytes = stackalloc byte[16];
            attributeValue.Slice(4, 16).CopyTo(ipBytes);

            if (xorAddress)
            {
                XorBytes(ipBytes.Slice(0, 4), MagicCookie, 0);
                XorBytes(ipBytes.Slice(4, 12), transactionId, 0);
            }

            endpoint = $"[{new IPAddress(ipBytes)}]:{port}";
            return true;
        }

        return false;
    }

    private static void XorBytes(Span<byte> target, ReadOnlySpan<byte> source, int startIndex)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] ^= source[startIndex + i];
        }
    }

    private static bool TryParseEndpoint(string? endpoint, out string ip, out int port)
    {
        ip = string.Empty;
        port = -1;

        if (string.IsNullOrEmpty(endpoint))
        {
            return false;
        }

        if (IPEndPoint.TryParse(endpoint, out var parsed))
        {
            ip = parsed.Address.ToString();
            port = parsed.Port;
            return true;
        }

        return false;
    }

    private static async Task<(Socks5UdpChannel.Socks5RemoteEndpoint Remote, byte[] Data)> ReceiveWithTimeoutAsync(
        Socks5UdpChannel channel,
        CancellationToken externalCancellationToken)
    {
        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        receiveCts.CancelAfter(PerReceiveTimeout);

        try
        {
            return await channel.ReceiveAsync(receiveCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (externalCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"STUN receive timed out after {PerReceiveTimeout.TotalMilliseconds:0} ms.");
        }
    }

    private static byte[] BuildStunBindingRequest(byte[] transactionId)
    {
        var data = new byte[20];
        data[0] = BindingRequestMessageType0;
        data[1] = BindingRequestMessageType1;
        data[2] = 0x00; // Message Length (0 for request)
        data[3] = 0x00;
        Array.Copy(MagicCookie, 0, data, 4, MagicCookie.Length);
        Array.Copy(transactionId, 0, data, 8, transactionId.Length);
        return data;
    }

    private static byte[] BuildStunRequestWithChangeRequest(byte[] transactionId, byte changeRequestValue)
    {
        var data = new byte[28];
        data[0] = BindingRequestMessageType0;
        data[1] = BindingRequestMessageType1;
        data[2] = 0x00; // Message Length (8 for CHANGE_REQUEST attribute)
        data[3] = 0x08;
        Array.Copy(MagicCookie, 0, data, 4, MagicCookie.Length);
        Array.Copy(transactionId, 0, data, 8, transactionId.Length);
        // CHANGE_REQUEST Attribute
        data[20] = 0x00;
        data[21] = 0x03; // Type: CHANGE_REQUEST
        data[22] = 0x00; // Length: 4
        data[23] = 0x04;
        data[24] = 0x00;
        data[25] = 0x00;
        data[26] = 0x00;
        data[27] = changeRequestValue;
        return data;
    }

    private static bool IsValidStunResponse(byte[] response, byte[] transactionId)
    {
        return response is { Length: >= 20 } &&
               response[0] == BindingSuccessResponseType0 &&
               response[1] == BindingSuccessResponseType1 &&
               response.AsSpan(4, 4).SequenceEqual(MagicCookie) &&
               response.AsSpan(8, 12).SequenceEqual(transactionId);
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static (string host, ushort port) ParseHostAndPort(string targetServerHost)
    {
        if (string.IsNullOrEmpty(targetServerHost))
        {
            return (string.Empty, StunDefaultPort);
        }

        // Handle IPv6 format: [::1]:port or [2001:db8::1]:port
        if (targetServerHost.StartsWith('['))
        {
            var closeBracketIndex = targetServerHost.IndexOf(']');
            if (closeBracketIndex > 0)
            {
                var host = targetServerHost[1..closeBracketIndex];
                if (closeBracketIndex < targetServerHost.Length - 1 && targetServerHost[closeBracketIndex + 1] == ':')
                {
                    var portStr = targetServerHost[(closeBracketIndex + 2)..];
                    if (ushort.TryParse(portStr, out var port))
                    {
                        return (host, port);
                    }
                }
                return (host, StunDefaultPort);
            }
        }

        // Handle IPv4 or domain format: 1.1.1.1:53 or exam.com:333
        var lastColonIndex = targetServerHost.LastIndexOf(':');
        if (lastColonIndex > 0)
        {
            var host = targetServerHost[..lastColonIndex];
            var portStr = targetServerHost[(lastColonIndex + 1)..];
            if (ushort.TryParse(portStr, out var port))
            {
                return (host, port);
            }
        }

        // No port specified, use default
        return (targetServerHost, StunDefaultPort);
    }

    private static byte[] GenerateStunTransactionId()
    {
        var transactionId = new byte[12];
        RandomNumberGenerator.Fill(transactionId);
        return transactionId;
    }

    private static bool IsDomain(string? domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return false;
        }

        var ext = Path.GetExtension(domain);
        if (!string.IsNullOrEmpty(ext)
            && ext[1..].ToLowerInvariant() is "json" or "txt" or "xml" or "cfg" or "ini" or "log" or "yaml" or "yml" or "toml")
        {
            return false;
        }

        return Uri.CheckHostName(domain) == UriHostNameType.Dns;
    }
}

public record StunResult
{
    public bool Socks5UdpChannelCreated { get; init; }
    public bool BindingSuccess { get; init; }
    public SubResult FilteringBehavior { get; init; }
    public SubResult MappingBehavior { get; init; }
    public string? MappedAddress { get; init; }
}

public enum SubResult
{
    Failed,
    Unsupported,
    EndpointIndependent,
    AddressDependent,
    AddressAndPortDependent,
}
