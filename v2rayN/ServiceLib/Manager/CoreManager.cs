namespace ServiceLib.Manager;

/// <summary>
/// Core process processing class
/// </summary>
public class CoreManager
{
    private static readonly Lazy<CoreManager> _instance = new(() => new());
    public static CoreManager Instance => _instance.Value;
    private Config _config;
    private WindowsJob? _processJob;
    private ProcessService? _processService;
    private ProcessService? _processPreService;
    private bool _linuxSudo = false;
    private Func<bool, string, Task>? _updateFunc;
    private const string _tag = "CoreHandler";

    public async Task Init(Config config, Func<bool, string, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;

        //Copy the bin folder to the storage location (for init)
        if (Environment.GetEnvironmentVariable(Global.LocalAppData) == "1")
        {
            var fromPath = Utils.GetBaseDirectory("bin");
            var toPath = Utils.GetBinPath("");
            if (fromPath != toPath)
            {
                FileManager.CopyDirectory(fromPath, toPath, true, false);
            }
        }

        if (Utils.IsNonWindows())
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
            foreach (var it in coreInfo)
            {
                if (it.CoreType == ECoreType.v2rayN)
                {
                    if (Utils.UpgradeAppExists(out var upgradeFileName))
                    {
                        await Utils.SetLinuxChmod(upgradeFileName);
                    }
                    continue;
                }

                foreach (var name in it.CoreExes)
                {
                    var exe = Utils.GetBinPath(Utils.GetExeName(name), it.CoreType.ToString());
                    if (File.Exists(exe))
                    {
                        await Utils.SetLinuxChmod(exe);
                    }
                }
            }
        }
    }

    public async Task LoadCore(ProfileItem? node)
    {
        if (node == null)
        {
            await UpdateFunc(false, ResUI.CheckServerSettings);
            return;
        }

        // Create launch context and configure parameters
        var context = new CoreLaunchContext(node, _config);
        context.AdjustForConfigType();

        await UpdateFunc(false, $"{node.GetSummary()}");
        await UpdateFunc(false, $"{Utils.GetRuntimeInfo()}");
        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        await CoreStop();
        await Task.Delay(100);

        if (Utils.IsWindows() && _config.TunModeItem.EnableTun)
        {
            await Task.Delay(100);
            await WindowsUtils.RemoveTunDevice();
        }

        // Start main core
        if (!await CoreStart(context))
        {
            return;
        }

        // Start pre-core if needed
        if (!await CoreStartPreService(context))
        {
            await CoreStop(); // Clean up main core if pre-core fails
            return;
        }
        
        if (_processService != null)
        {
            await UpdateFunc(true, $"{node.GetSummary()}");
        }
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(List<ServerTestItem> selecteds)
    {
        var coreType = selecteds.Exists(t => t.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.Anytls) ? ECoreType.sing_box : ECoreType.Xray;
        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName, coreType);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, configPath, selecteds, coreType);
        await UpdateFunc(false, result.Msg);
        if (result.Success != true)
        {
            return null;
        }

        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        await UpdateFunc(false, configPath);

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(ServerTestItem testItem)
    {
        var node = await AppManager.Instance.GetProfileItem(testItem.IndexId);
        if (node is null)
        {
            return null;
        }

        var context = new CoreLaunchContext(node, _config);
        context.AdjustForConfigType();
        var coreType = context.GetOutboundCoreType();
        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName, coreType);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, context, testItem, configPath);
        if (result.Success != true)
        {
            return null;
        }

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task CoreStop()
    {
        try
        {
            if (_linuxSudo)
            {
                await CoreAdminManager.Instance.KillProcessAsLinuxSudo();
                _linuxSudo = false;
            }

            if (_processService != null)
            {
                await _processService.StopAsync();
                _processService.Dispose();
                _processService = null;
            }

            if (_processPreService != null)
            {
                await _processPreService.StopAsync();
                _processPreService.Dispose();
                _processPreService = null;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #region Private

    private async Task<bool> CoreStart(CoreLaunchContext context)
    {
        var coreType = context.GetOutboundCoreType();
        var fileName = Utils.GetBinConfigPath(Global.CoreConfigFileName, coreType);
        var result = context.OutboundCorePassThroughOnly
            ? await CoreConfigHandler.GeneratePassthroughConfig(context, fileName)
            : await CoreConfigHandler.GenerateClientConfig(context, fileName);

        if (result.Success != true)
        {
            await UpdateFunc(true, result.Msg);
            return false;
        }

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(context.GetOutboundCoreType());
        var displayLog = context.Node.ConfigType != EConfigType.Custom || context.Node.DisplayLog;
        var proc = await RunProcess(coreInfo, Utils.GetBinConfigFileName(Global.CoreConfigFileName, coreType), displayLog, true);
        
        if (proc is null)
        {
            await UpdateFunc(true, ResUI.FailedToRunCore);
            return false;
        }
        
        _processService = proc;
        _config.RunningCoreType = context.GetInRouteCoreType();
        return true;
    }

    private async Task<bool> CoreStartPreService(CoreLaunchContext context)
    {
        if (_processService == null || _processService.HasExited)
        {
            return true;
        }
        if (!context.SplitCore)
        {
            return true; // No pre-core needed, consider successful
        }

        var coreType = context.GetInRouteCoreType();
        var fileName = Utils.GetBinConfigPath(Global.CorePreConfigFileName, coreType);

        var tun2SocksAddress = context.Node.Address;
        if (context.Node.ConfigType > EConfigType.Group)
        {
            var lstAddresses = (await ProfileGroupItemManager.GetAllChildDomainAddresses(context.Node.IndexId)).ToList();
            if (lstAddresses.Count > 0)
            {
                tun2SocksAddress = Utils.List2String(lstAddresses);
            }
        }

        var itemSocks = new ProfileItem()
        {
            CoreType = coreType,
            ConfigType = EConfigType.SOCKS,
            Address = Global.Loopback,
            SpiderX = tun2SocksAddress, // Tun2SocksAddress
            Port = context.PreSocksPort
        };
        var itemSocksLaunch = new CoreLaunchContext(itemSocks, _config);

        var result = await CoreConfigHandler.GenerateClientConfig(itemSocksLaunch, fileName);
        if (!result.Success)
        {
            await UpdateFunc(true, result.Msg);
            return false;
        }

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        var proc = await RunProcess(coreInfo, Utils.GetBinConfigFileName(Global.CorePreConfigFileName, coreType), true, true);

        if (proc is null || _processService.HasExited)
        {
            await UpdateFunc(true, ResUI.FailedToRunCore);
            return false;
        }
        
        _processPreService = proc;
        return true;
    }

    private async Task UpdateFunc(bool notify, string msg)
    {
        await _updateFunc?.Invoke(notify, msg);
    }

    #endregion Private

    #region Process

    private async Task<ProcessService?> RunProcess(CoreInfo? coreInfo, string configPath, bool displayLog, bool mayNeedSudo)
    {
        var fileName = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var msg);
        if (fileName.IsNullOrEmpty())
        {
            await UpdateFunc(false, msg);
            return null;
        }

        try
        {
            if (mayNeedSudo
                && _config.TunModeItem.EnableTun
                && coreInfo.CoreType == ECoreType.sing_box
                && Utils.IsNonWindows())
            {
                _linuxSudo = true;
                await CoreAdminManager.Instance.Init(_config, _updateFunc);
                return await CoreAdminManager.Instance.RunProcessAsLinuxSudo(fileName, coreInfo, configPath);
            }

            return await RunProcessNormal(fileName, coreInfo, configPath, displayLog);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await UpdateFunc(mayNeedSudo, ex.Message);
            return null;
        }
    }

    private async Task<ProcessService?> RunProcessNormal(string fileName, CoreInfo? coreInfo, string configPath, bool displayLog)
    {
        var environmentVars = new Dictionary<string, string>();
        foreach (var kv in coreInfo.Environment)
        {
            environmentVars[kv.Key] = string.Format(kv.Value, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath);
        }

        var procService = new ProcessService(
            fileName: fileName,
            arguments: string.Format(coreInfo.Arguments, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath, coreInfo.CoreType).AppendQuotes() : configPath),
            workingDirectory: Utils.GetBinConfigPath(),
            displayLog: displayLog,
            redirectInput: false,
            environmentVars: environmentVars,
            updateFunc: _updateFunc
        );

        await procService.StartAsync();

        await Task.Delay(100);

        if (procService is null or { HasExited: true })
        {
            throw new Exception(ResUI.FailedToRunCore);
        }
        AddProcessJob(procService.Handle);

        return procService;
    }

    private void AddProcessJob(nint processHandle)
    {
        if (Utils.IsWindows())
        {
            _processJob ??= new();
            try
            {
                _processJob?.AddProcess(processHandle);
            }
            catch { }
        }
    }

    #endregion Process
}
