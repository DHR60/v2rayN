using System.Diagnostics;
using System.Text;
using DynamicData;
using ServiceLib.Enums;
using ServiceLib.Models;
using static SQLite.SQLite3;

namespace ServiceLib.Manager;

/// <summary>
/// Core process processing class
/// </summary>
public class CoreManager
{
    private static readonly Lazy<CoreManager> _instance = new(() => new());
    public static CoreManager Instance => _instance.Value;
    private Config _config;
    private Process? _process;
    private Process? _processPre;
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

        if (_process != null)
        {
            await UpdateFunc(true, $"{node.GetSummary()}");
        }
    }

    public async Task<int> LoadCoreConfigSpeedtest(List<ServerTestItem> selecteds)
    {
        var coreType = selecteds.Exists(t => t.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.Anytls) ? ECoreType.sing_box : ECoreType.Xray;
        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName, coreType);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, configPath, selecteds, coreType);
        await UpdateFunc(false, result.Msg);
        if (result.Success != true)
        {
            return -1;
        }

        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        await UpdateFunc(false, configPath);

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        var proc = await RunProcess(coreInfo, fileName, true, false);
        if (proc is null)
        {
            return -1;
        }

        return proc.Id;
    }

    public async Task<int> LoadCoreConfigSpeedtest(ServerTestItem testItem)
    {
        var node = await AppManager.Instance.GetProfileItem(testItem.IndexId);
        if (node is null)
        {
            return -1;
        }

        var context = new CoreLaunchContext(node, _config);
        context.AdjustForConfigType();
        var coreType = context.GetOutboundCoreType();
        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName, coreType);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, context, testItem, configPath);
        if (result.Success != true)
        {
            return -1;
        }

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        var proc = await RunProcess(coreInfo, fileName, true, false);
        if (proc is null)
        {
            return -1;
        }

        return proc.Id;
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

            if (_process != null)
            {
                await ProcUtils.ProcessKill(_process, Utils.IsWindows());
                _process = null;
            }

            if (_processPre != null)
            {
                await ProcUtils.ProcessKill(_processPre, Utils.IsWindows());
                _processPre = null;
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
        
        _process = proc;
        _config.RunningCoreType = context.GetInRouteCoreType();
        return true;
    }

    private async Task<bool> CoreStartPreService(CoreLaunchContext context)
    {
        if (!context.SplitCore)
        {
            return true; // No pre-core needed, consider successful
        }

        var coreType = context.GetInRouteCoreType();
        var fileName = Utils.GetBinConfigPath(Global.CorePreConfigFileName, coreType);

        var tun2SocksAddress = context.Node.Address;
        if (context.Node.ConfigType > EConfigType.Group)
        {
            static async Task<List<string>> GetChildNodeAddressesAsync(string parentIndexId)
            {
                var childAddresses = new List<string>();
                if (!ProfileGroupItemManager.Instance.TryGet(parentIndexId, out var groupItem) || groupItem.ChildItems.IsNullOrEmpty())
                    return childAddresses;

                var childIds = Utils.String2List(groupItem.ChildItems);

                foreach (var childId in childIds)
                {
                    var childNode = await AppManager.Instance.GetProfileItem(childId);
                    if (childNode == null)
                        continue;

                    if (!childNode.IsComplex())
                    {
                        childAddresses.Add(childNode.Address);
                    }
                    else if (childNode.ConfigType > EConfigType.Group)
                    {
                        var subAddresses = await GetChildNodeAddressesAsync(childNode.IndexId);
                        childAddresses.AddRange(subAddresses);
                    }
                }

                return childAddresses;
            }

            var lstAddresses = await GetChildNodeAddressesAsync(context.Node.IndexId);
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

        if (proc is null || (_process?.HasExited == true))
        {
            await UpdateFunc(true, ResUI.FailedToRunCore);
            return false;
        }
        
        _processPre = proc;
        return true;
    }

    private async Task UpdateFunc(bool notify, string msg)
    {
        await _updateFunc?.Invoke(notify, msg);
    }

    #endregion Private

    #region Process

    private async Task<Process?> RunProcess(CoreInfo? coreInfo, string configPath, bool displayLog, bool mayNeedSudo)
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

    private async Task<Process?> RunProcessNormal(string fileName, CoreInfo? coreInfo, string configPath, bool displayLog)
    {
        Process proc = new()
        {
            StartInfo = new()
            {
                FileName = fileName,
                Arguments = string.Format(coreInfo.Arguments, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath, coreInfo.CoreType).AppendQuotes() : configPath),
                WorkingDirectory = Utils.GetBinConfigPath(),
                UseShellExecute = false,
                RedirectStandardOutput = displayLog,
                RedirectStandardError = displayLog,
                CreateNoWindow = true,
                StandardOutputEncoding = displayLog ? Encoding.UTF8 : null,
                StandardErrorEncoding = displayLog ? Encoding.UTF8 : null,
            }
        };
        foreach (var kv in coreInfo.Environment)
        {
            proc.StartInfo.Environment[kv.Key] = string.Format(kv.Value, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath);
        }

        if (displayLog)
        {
            void dataHandler(object sender, DataReceivedEventArgs e)
            {
                if (e.Data.IsNotEmpty())
                {
                    _ = UpdateFunc(false, e.Data + Environment.NewLine);
                }
            }
            proc.OutputDataReceived += dataHandler;
            proc.ErrorDataReceived += dataHandler;
        }
        proc.Start();

        if (displayLog)
        {
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        await Task.Delay(100);
        AppManager.Instance.AddProcess(proc.Handle);
        if (proc is null or { HasExited: true })
        {
            throw new Exception(ResUI.FailedToRunCore);
        }
        return proc;
    }

    #endregion Process
}
