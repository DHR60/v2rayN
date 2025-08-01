using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace ServiceLib.Services;

public class SpeedtestService
{
    private static readonly string _tag = "SpeedtestService";
    private Config? _config;
    private Action<SpeedTestResult>? _updateFunc;
    private static readonly ConcurrentBag<string> _lstExitLoop = new();

    public SpeedtestService(Config config, Action<SpeedTestResult> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;
    }

    public void RunLoop(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        Task.Run(async () =>
        {
            await RunAsync(actionType, selecteds);
            await ProfileExHandler.Instance.SaveTo();
            UpdateFunc("", ResUI.SpeedtestingCompleted);
        });
    }

    public void ExitLoop()
    {
        if (_lstExitLoop.Count > 0)
        {
            UpdateFunc("", ResUI.SpeedtestingStop);

            _lstExitLoop.Clear();
        }
    }

    private async Task RunAsync(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        var exitLoopKey = Utils.GetGuid(false);
        _lstExitLoop.Add(exitLoopKey);

        var lstSelected = GetClearItem(actionType, selecteds);

        switch (actionType)
        {
            case ESpeedActionType.Tcping:
                await RunTcpingAsync(lstSelected);
                break;

            case ESpeedActionType.Realping:
                await RunRealPingBatchAsync(lstSelected, exitLoopKey);
                break;

            case ESpeedActionType.Speedtest:
                await RunMixedTestAsync(lstSelected, 1, true, exitLoopKey);
                break;

            case ESpeedActionType.Mixedtest:
                await RunMixedTestAsync(lstSelected, _config.SpeedTestItem.MixedConcurrencyCount, true, exitLoopKey);
                break;

            case ESpeedActionType.Udptest:
                await RunUdpTestBatchAsync(lstSelected, exitLoopKey);
                break;
        }
    }

    private List<ServerTestItem> GetClearItem(ESpeedActionType actionType, List<ProfileItem> selecteds)
    {
        var lstSelected = new List<ServerTestItem>();
        foreach (var it in selecteds)
        {
            if (!(Global.XraySupportConfigType.Contains(it.ConfigType) || Global.SingboxSupportConfigType.Contains(it.ConfigType)))
            {
                continue;
            }

            if (it.Port <= 0)
            {
                continue;
            }

            lstSelected.Add(new ServerTestItem()
            {
                IndexId = it.IndexId,
                Address = it.Address,
                Port = it.Port,
                ConfigType = it.ConfigType,
                QueueNum = selecteds.IndexOf(it)
            });
        }

        //clear test result
        foreach (var it in lstSelected)
        {
            switch (actionType)
            {
                case ESpeedActionType.Tcping:
                case ESpeedActionType.Udptest:
                case ESpeedActionType.Realping:
                    UpdateFunc(it.IndexId, ResUI.Speedtesting, "");
                    ProfileExHandler.Instance.SetTestDelay(it.IndexId, 0);
                    break;

                case ESpeedActionType.Speedtest:
                    UpdateFunc(it.IndexId, "", ResUI.SpeedtestingWait);
                    ProfileExHandler.Instance.SetTestSpeed(it.IndexId, 0);
                    break;

                case ESpeedActionType.Mixedtest:
                    UpdateFunc(it.IndexId, ResUI.Speedtesting, ResUI.SpeedtestingWait);
                    ProfileExHandler.Instance.SetTestDelay(it.IndexId, 0);
                    ProfileExHandler.Instance.SetTestSpeed(it.IndexId, 0);
                    break;
            }
        }

        return lstSelected;
    }

    private async Task RunTcpingAsync(List<ServerTestItem> selecteds)
    {
        List<Task> tasks = [];
        foreach (var it in selecteds)
        {
            if (!(Global.XraySupportConfigType.Contains(it.ConfigType) || Global.SingboxSupportConfigType.Contains(it.ConfigType)))
            {
                continue;
            }
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var responseTime = await GetTcpingTime(it.Address, it.Port);

                    ProfileExHandler.Instance.SetTestDelay(it.IndexId, responseTime);
                    UpdateFunc(it.IndexId, responseTime.ToString());
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
            }));
        }
        await Task.WhenAll(tasks);
    }

    private async Task RunRealPingBatchAsync(List<ServerTestItem> lstSelected, string exitLoopKey, int pageSize = 0)
    {
        if (pageSize <= 0)
        {
            pageSize = lstSelected.Count < Global.SpeedTestPageSize ? lstSelected.Count : Global.SpeedTestPageSize;
        }
        var lstTest = GetTestBatchItem(lstSelected, pageSize);

        List<ServerTestItem> lstFailed = new();
        foreach (var lst in lstTest)
        {
            var ret = await RunRealPingAsync(lst, exitLoopKey);
            if (ret == false)
            {
                lstFailed.AddRange(lst);
            }
            await Task.Delay(100);
        }

        //Retest the failed part
        var pageSizeNext = pageSize / 2;
        if (lstFailed.Count > 0 && pageSizeNext > 0)
        {
            if (_lstExitLoop.Any(p => p == exitLoopKey) == false)
            {
                UpdateFunc("", ResUI.SpeedtestingSkip);
                return;
            }

            UpdateFunc("", string.Format(ResUI.SpeedtestingTestFailedPart, lstFailed.Count));

            if (pageSizeNext > _config.SpeedTestItem.MixedConcurrencyCount)
            {
                await RunRealPingBatchAsync(lstFailed, exitLoopKey, pageSizeNext);
            }
            else
            {
                await RunMixedTestAsync(lstSelected, _config.SpeedTestItem.MixedConcurrencyCount, false, exitLoopKey);
            }
        }
    }

    private async Task<bool> RunRealPingAsync(List<ServerTestItem> selecteds, string exitLoopKey)
    {
        var pid = -1;
        try
        {
            pid = await CoreHandler.Instance.LoadCoreConfigSpeedtest(selecteds);
            if (pid < 0)
            {
                return false;
            }
            await Task.Delay(1000);

            var downloadHandle = new DownloadService();

            List<Task> tasks = new();
            foreach (var it in selecteds)
            {
                if (!it.AllowTest)
                {
                    continue;
                }
                if (!(Global.XraySupportConfigType.Contains(it.ConfigType) || Global.SingboxSupportConfigType.Contains(it.ConfigType)))
                {
                    continue;
                }
                tasks.Add(Task.Run(async () =>
                {
                    await DoRealPing(downloadHandle, it);
                }));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            if (pid > 0)
            {
                await ProcUtils.ProcessKill(pid);
            }
        }
        return true;
    }

    private async Task RunMixedTestAsync(List<ServerTestItem> selecteds, int concurrencyCount, bool blSpeedTest, string exitLoopKey)
    {
        using var concurrencySemaphore = new SemaphoreSlim(concurrencyCount);
        var downloadHandle = new DownloadService();
        List<Task> tasks = new();
        foreach (var it in selecteds)
        {
            if (_lstExitLoop.Any(p => p == exitLoopKey) == false)
            {
                UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                continue;
            }
            if (!(Global.XraySupportConfigType.Contains(it.ConfigType) || Global.SingboxSupportConfigType.Contains(it.ConfigType)))
            {
                continue;
            }
            await concurrencySemaphore.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                var pid = -1;
                try
                {
                    pid = await CoreHandler.Instance.LoadCoreConfigSpeedtest(it);
                    if (pid < 0)
                    {
                        UpdateFunc(it.IndexId, "", ResUI.FailedToRunCore);
                    }
                    else
                    {
                        await Task.Delay(1000);
                        var delay = await DoRealPing(downloadHandle, it);
                        if (blSpeedTest)
                        {
                            if (delay > 0)
                            {
                                await DoSpeedTest(downloadHandle, it);
                            }
                            else
                            {
                                UpdateFunc(it.IndexId, "", ResUI.SpeedtestingSkip);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
                finally
                {
                    if (pid > 0)
                    {
                        await ProcUtils.ProcessKill(pid);
                    }
                    concurrencySemaphore.Release();
                }
            }));
        }
        await Task.WhenAll(tasks);
    }

    private async Task RunUdpTestAsync(List<ServerTestItem> selectedProxies, string exitLoopKey)
    {
        var concurrencyCount = Math.Max(1, _config.NtpTestItem.ConcurrencyCount);
        using var concurrencySemaphore = new SemaphoreSlim(concurrencyCount);
        var tasks = new List<Task>();

        foreach (var item in selectedProxies)
        {
            if (!_lstExitLoop.Contains(exitLoopKey))
            {
                continue;
            }

            if (item.ConfigType == EConfigType.Custom)
            {
                continue;
            }

            await concurrencySemaphore.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                var currentItemId = item.IndexId ?? string.Empty;
                var pid = -1;
                try
                {
                    if (currentItemId.IsNotEmpty())
                    {
                        UpdateFunc(currentItemId, "");
                    }

                    pid = await CoreHandler.Instance.LoadCoreConfigSpeedtest(item);
                    if (pid < 0)
                    {
                        if (currentItemId.IsNotEmpty())
                        {
                            UpdateFunc(currentItemId, "-1");
                            ProfileExHandler.Instance.SetTestDelay(currentItemId, -1);
                        }
                        return; // Exit this Task.Run lambda
                    }

                    await Task.Delay(500);

                    await PerformNtpTestForItemAsync(item);
                }
                catch (Exception ex)
                {
                    if (currentItemId.IsNotEmpty())
                    {
                        UpdateFunc(currentItemId, "-1");
                        if (ex is not OperationCanceledException or TimeoutException)
                        {
                            Logging.SaveLog(_tag, ex);
                        }

                        ProfileExHandler.Instance.SetTestDelay(currentItemId, -1);
                    }
                }
                finally
                {
                    if (pid > 0)
                    {
                        await ProcUtils.ProcessKill(pid);
                    }
                    concurrencySemaphore.Release();
                }
            }));
        }
        await Task.WhenAll(tasks);
    }

    private async Task<int> DoRealPing(DownloadService downloadHandle, ServerTestItem it)
    {
        var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
        var responseTime = await downloadHandle.GetRealPingTime(_config.SpeedTestItem.SpeedPingTestUrl, webProxy, 10);

        ProfileExHandler.Instance.SetTestDelay(it.IndexId, responseTime);
        UpdateFunc(it.IndexId, responseTime.ToString());
        return responseTime;
    }

    private async Task DoSpeedTest(DownloadService downloadHandle, ServerTestItem it)
    {
        UpdateFunc(it.IndexId, "", ResUI.Speedtesting);

        var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
        var url = _config.SpeedTestItem.SpeedTestUrl;
        var timeout = _config.SpeedTestItem.SpeedTestTimeout;
        await downloadHandle.DownloadDataAsync(url, webProxy, timeout, (success, msg) =>
        {
            decimal.TryParse(msg, out var dec);
            if (dec > 0)
            {
                ProfileExHandler.Instance.SetTestSpeed(it.IndexId, dec);
            }
            UpdateFunc(it.IndexId, "", msg);
        });
    }

    private async Task<int> GetTcpingTime(string url, int port)
    {
        var responseTime = -1;

        try
        {
            if (!IPAddress.TryParse(url, out var ipAddress))
            {
                var ipHostInfo = await Dns.GetHostEntryAsync(url);
                ipAddress = ipHostInfo.AddressList.First();
            }

            IPEndPoint endPoint = new(ipAddress, port);
            using Socket clientSocket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            var timer = Stopwatch.StartNew();
            var result = clientSocket.BeginConnect(endPoint, null, null);
            if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("connect timeout (5s): " + url);
            }
            timer.Stop();
            responseTime = (int)timer.Elapsed.TotalMilliseconds;

            clientSocket.EndConnect(result);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return responseTime;
    }

    private List<List<ServerTestItem>> GetTestBatchItem(List<ServerTestItem> lstSelected, int pageSize)
    {
        List<List<ServerTestItem>> lstTest = new();
        var lst1 = lstSelected.Where(t => Global.XraySupportConfigType.Contains(t.ConfigType)).ToList();
        var lst2 = lstSelected.Where(t => Global.SingboxSupportConfigType.Contains(t.ConfigType) && !Global.XraySupportConfigType.Contains(t.ConfigType)).ToList();

        for (var num = 0; num < (int)Math.Ceiling(lst1.Count * 1.0 / pageSize); num++)
        {
            lstTest.Add(lst1.Skip(num * pageSize).Take(pageSize).ToList());
        }
        for (var num = 0; num < (int)Math.Ceiling(lst2.Count * 1.0 / pageSize); num++)
        {
            lstTest.Add(lst2.Skip(num * pageSize).Take(pageSize).ToList());
        }

        return lstTest;
    }
    private async Task RunUdpTestBatchAsync(List<ServerTestItem> lstSelected, string exitLoopKey, int pageSize = 0)
    {
        if (pageSize <= 0)
        {
            pageSize = lstSelected.Count < Global.SpeedTestPageSize ? lstSelected.Count : Global.SpeedTestPageSize;
        }
        var lstTest = GetTestBatchItem(lstSelected, pageSize);

        List<ServerTestItem> lstFailed = new();
        foreach (var lst in lstTest)
        {
            var ret = await RunUdpTestForBatchAsync(lst, exitLoopKey);
            if (ret == false)
            {
                lstFailed.AddRange(lst);
            }
            await Task.Delay(100);
        }
    }

    private async Task<bool> RunUdpTestForBatchAsync(List<ServerTestItem> selecteds, string exitLoopKey)
    {
        var pid = -1;
        try
        {
            pid = await CoreHandler.Instance.LoadCoreConfigSpeedtest(selecteds);
            if (pid < 0)
            {
                return false;
            }

            await Task.Delay(500);

            List<Task> tasks = new();
            foreach (var it in selecteds)
            {
                if (!_lstExitLoop.Contains(exitLoopKey))
                {
                    continue;
                }

                if (it.ConfigType == EConfigType.Custom)
                {
                    continue;
                }

                var currentItemId = it.IndexId ?? string.Empty;
                if (currentItemId.IsNotEmpty())
                {
                    UpdateFunc(currentItemId, "");
                }

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await PerformNtpTestForItemAsync(it);
                    }
                    catch (Exception ex)
                    {
                        if (currentItemId.IsNotEmpty())
                        {
                            UpdateFunc(currentItemId, "-1");
                            if (ex is not OperationCanceledException or TimeoutException)
                            {
                                Logging.SaveLog(_tag, ex);
                            }

                            ProfileExHandler.Instance.SetTestDelay(currentItemId, -1);
                        }
                    }
                }));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
        finally
        {
            if (pid > 0)
            {
                await ProcUtils.ProcessKill(pid);
            }
        }
        return true;
    }

    private async Task PerformNtpTestForItemAsync(ServerTestItem proxyProfileItem)
    {
        var socks5Host = Global.Loopback;
        var socks5Port = proxyProfileItem.Port;
        var targetNtpServer = _config.NtpTestItem.NtpServer;
        var currentIndexId = proxyProfileItem.IndexId ?? string.Empty;

        var ntpClient = new NtpOverSocks5(socks5Host, socks5Port);

        var result = await ntpClient.GetNtpTimeAsync(
            targetNtpServer,
            TimeSpan.FromSeconds(_config.NtpTestItem.TimeoutSeconds)
        );

        if (result.Success)
        {
            var rttStr = result.RoundTripTime?.TotalMilliseconds.ToString("F0") ?? "-1";
            ProfileExHandler.Instance.SetTestDelay(currentIndexId, (int)(result.RoundTripTime?.TotalMilliseconds ?? -1));
            UpdateFunc(currentIndexId, rttStr);
        }
        else
        {
            ProfileExHandler.Instance.SetTestDelay(currentIndexId, -1);
            UpdateFunc(currentIndexId, "-1");
        }
    }

    private void UpdateFunc(string indexId, string delay, string speed = "")
    {
        _updateFunc?.Invoke(new() { IndexId = indexId, Delay = delay, Speed = speed });
        if (indexId.IsNotEmpty() && speed.IsNotEmpty())
        {
            ProfileExHandler.Instance.SetTestMessage(indexId, speed);
        }
    }
}
