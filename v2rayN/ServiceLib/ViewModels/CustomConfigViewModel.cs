using System.Reactive;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace ServiceLib.ViewModels;
public class CustomConfigViewModel : MyReactiveObject
{
    #region Reactive
    [Reactive]
    public bool EnableCustomConfig4Ray { get; set; }

    [Reactive]
    public bool EnableCustomConfig4Singbox { get; set; }

    [Reactive]
    public string CustomConfig4Ray { get; set; }

    [Reactive]
    public string CustomConfig4Singbox { get; set; }

    [Reactive]
    public string CustomTunConfig4Singbox { get; set; }

    public ReactiveCommand<Unit, Unit> SaveCmd { get; }
    #endregion Reactive

    public CustomConfigViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppHandler.Instance.Config;
        _updateView = updateView;
        SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveSettingAsync();
        });

        _ = Init();
    }
    private async Task Init()
    {
        var item = await AppHandler.Instance.GetCustomConfigItem(ECoreType.Xray);
        EnableCustomConfig4Ray = item?.Enabled ?? false;
        CustomConfig4Ray = item?.Config ?? string.Empty;

        var item2 = await AppHandler.Instance.GetCustomConfigItem(ECoreType.sing_box);
        EnableCustomConfig4Singbox = item2?.Enabled ?? false;
        CustomConfig4Singbox = item2?.Config ?? string.Empty;
        CustomTunConfig4Singbox = item2?.TunConfig ?? string.Empty;
    }

    private async Task SaveSettingAsync()
    {
        if (!await SaveXrayConfigAsync())
            return;

        if (!await SaveSingboxConfigAsync())
            return;

        NoticeHandler.Instance.Enqueue(ResUI.OperationSuccess);
        _ = _updateView?.Invoke(EViewAction.CloseWindow, null);
    }

    private async Task<bool> SaveXrayConfigAsync()
    {
        var item = await AppHandler.Instance.GetCustomConfigItem(ECoreType.Xray);
        item.Enabled = EnableCustomConfig4Ray;
        item.Config = null;

        if (CustomConfig4Ray.IsNotEmpty())
        {
            if (!TrySerializeConfig<V2rayConfig>(CustomConfig4Ray, out var serializedConfig))
                return false;
            
            item.Config = serializedConfig;
        }

        await ConfigHandler.SaveCustomConfigItem(_config, item);
        return true;
    }

    private async Task<bool> SaveSingboxConfigAsync()
    {
        var item = await AppHandler.Instance.GetCustomConfigItem(ECoreType.sing_box);
        item.Enabled = EnableCustomConfig4Singbox;
        item.Config = null;
        item.TunConfig = null;

        var hasChanges = false;

        if (CustomConfig4Singbox.IsNotEmpty())
        {
            if (!TrySerializeConfig<SingboxConfig>(CustomConfig4Singbox, out var serializedConfig))
                return false;
            
            item.Config = serializedConfig;
            hasChanges = true;
        }

        if (CustomTunConfig4Singbox.IsNotEmpty())
        {
            if (!TrySerializeConfig<SingboxConfig>(CustomTunConfig4Singbox, out var serializedTunConfig))
                return false;
            
            item.TunConfig = serializedTunConfig;
            hasChanges = true;
        }

        if (hasChanges)
        {
            await ConfigHandler.SaveCustomConfigItem(_config, item);
        }

        return true;
    }

    private bool TrySerializeConfig<T>(string configText, out string? serializedConfig)
    {
        serializedConfig = null;
        var obj = JsonUtils.Deserialize<T>(configText);
        
        if (obj == null)
        {
            NoticeHandler.Instance.Enqueue(ResUI.FillCorrectConfigText);
            return false;
        }

        serializedConfig = JsonUtils.Serialize(obj);
        return true;
    }
}
