using System.Reactive;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace ServiceLib.ViewModels;

public class RoutingSettingViewModel : MyReactiveObject
{
    #region Reactive

    private IObservableCollection<RoutingItemModel> _routingItems = new ObservableCollectionExtended<RoutingItemModel>();
    public IObservableCollection<RoutingItemModel> RoutingItems => _routingItems;

    [Reactive]
    public RoutingItemModel SelectedSource { get; set; }

    public IList<RoutingItemModel> SelectedSources { get; set; }

    [Reactive]
    public string DomainStrategy { get; set; }

    [Reactive]
    public string DomainMatcher { get; set; }

    [Reactive]
    public string DomainStrategy4Singbox { get; set; }

    [Reactive]
    public string RawRoute4Ray { get; set; }

    [Reactive]
    public string RawRoute4Singbox { get; set; }

    [Reactive]
    public string RawTunRoute4Singbox { get; set; }

    [Reactive]
    public bool EnableRawRoute4Ray { get; set; }

    [Reactive]
    public bool EnableRawRoute4Singbox { get; set; }

    public ReactiveCommand<Unit, Unit> RoutingAdvancedAddCmd { get; }
    public ReactiveCommand<Unit, Unit> RoutingAdvancedRemoveCmd { get; }
    public ReactiveCommand<Unit, Unit> RoutingAdvancedSetDefaultCmd { get; }
    public ReactiveCommand<Unit, Unit> RoutingAdvancedImportRulesCmd { get; }

    public ReactiveCommand<Unit, Unit> SaveCmd { get; }
    public bool IsModified { get; set; }

    #endregion Reactive

    public RoutingSettingViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppHandler.Instance.Config;
        _updateView = updateView;

        var canEditRemove = this.WhenAnyValue(
            x => x.SelectedSource,
            selectedSource => selectedSource != null && !selectedSource.Remarks.IsNullOrEmpty());

        RoutingAdvancedAddCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RoutingAdvancedEditAsync(true);
        });
        RoutingAdvancedRemoveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RoutingAdvancedRemoveAsync();
        }, canEditRemove);
        RoutingAdvancedSetDefaultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RoutingAdvancedSetDefault();
        }, canEditRemove);
        RoutingAdvancedImportRulesCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RoutingAdvancedImportRules();
        });

        SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveRoutingAsync();
        });

        _ = Init();
    }

    private async Task Init()
    {
        SelectedSource = new();

        DomainStrategy = _config.RoutingBasicItem.DomainStrategy;
        DomainMatcher = _config.RoutingBasicItem.DomainMatcher;
        DomainStrategy4Singbox = _config.RoutingBasicItem.DomainStrategy4Singbox;

        var rawRouteItem = await AppHandler.Instance.GetRawRouteItem(ECoreType.Xray);
        if (rawRouteItem != null)
        {
            EnableRawRoute4Ray = rawRouteItem.Enabled;
            RawRoute4Ray = rawRouteItem.Route;
        }
        var rawRouteItem2 = await AppHandler.Instance.GetRawRouteItem(ECoreType.sing_box);
        if (rawRouteItem2 != null)
        {
            EnableRawRoute4Singbox = rawRouteItem2.Enabled;
            RawRoute4Singbox = rawRouteItem2.Route;
            RawTunRoute4Singbox = rawRouteItem2.TunRoute;
        }

        await ConfigHandler.InitBuiltinRouting(_config);
        await RefreshRoutingItems();
    }

    #region Refresh Save

    public async Task RefreshRoutingItems()
    {
        _routingItems.Clear();

        var routings = await AppHandler.Instance.RoutingItems();
        foreach (var item in routings)
        {
            var it = new RoutingItemModel()
            {
                IsActive = item.IsActive,
                RuleNum = item.RuleNum,
                Id = item.Id,
                Remarks = item.Remarks,
                Url = item.Url,
                CustomIcon = item.CustomIcon,
                CustomRulesetPath4Singbox = item.CustomRulesetPath4Singbox,
                Sort = item.Sort,
            };
            _routingItems.Add(it);
        }
    }

    private async Task SaveRoutingAsync()
    {
        _config.RoutingBasicItem.DomainStrategy = DomainStrategy;
        _config.RoutingBasicItem.DomainMatcher = DomainMatcher;
        _config.RoutingBasicItem.DomainStrategy4Singbox = DomainStrategy4Singbox;

        var item = await AppHandler.Instance.GetRawRouteItem(ECoreType.Xray);
        item.Enabled = EnableRawRoute4Ray;
        item.Route = null;
        if (RawRoute4Ray.IsNotEmpty())
        {
            var obj = JsonUtils.Deserialize<Routing4Ray>(RawRoute4Ray);
            if (obj != null)
            {
                item.Route = JsonUtils.Serialize(obj);
                await ConfigHandler.SaveRawRouteItem(_config, item);
            }
            else
            {
                NoticeHandler.Instance.Enqueue(ResUI.FillCorrectRoutingText);
            }
        }
        var item2 = await AppHandler.Instance.GetRawRouteItem(ECoreType.sing_box);
        item2.Enabled = EnableRawRoute4Singbox;
        item2.Route = null;
        item2.TunRoute = null;

        var changed = false;

        if (RawRoute4Singbox.IsNotEmpty())
        {
            var obj2 = JsonUtils.Deserialize<Route4Sbox>(RawRoute4Singbox);
            if (obj2 != null)
            {
                item2.Route = JsonUtils.Serialize(obj2);
                changed = true;
            }
            else
            {
                NoticeHandler.Instance.Enqueue(ResUI.FillCorrectRoutingText);
            }
        }

        if (RawTunRoute4Singbox.IsNotEmpty())
        {
            var obj3 = JsonUtils.Deserialize<Route4Sbox>(RawTunRoute4Singbox);
            if (obj3 != null)
            {
                item2.TunRoute = JsonUtils.Serialize(obj3);
                changed = true;
            }
            else
            {
                NoticeHandler.Instance.Enqueue(ResUI.FillCorrectRoutingText);
            }
        }

        if (changed)
        {
            await ConfigHandler.SaveRawRouteItem(_config, item2);
        }

        if (await ConfigHandler.SaveConfig(_config) == 0)
        {
            NoticeHandler.Instance.Enqueue(ResUI.OperationSuccess);
            _updateView?.Invoke(EViewAction.CloseWindow, null);
        }
        else
        {
            NoticeHandler.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    #endregion Refresh Save

    public async Task RoutingAdvancedEditAsync(bool blNew)
    {
        RoutingItem item;
        if (blNew)
        {
            item = new();
        }
        else
        {
            item = await AppHandler.Instance.GetRoutingItem(SelectedSource?.Id);
            if (item is null)
            {
                return;
            }
        }
        if (await _updateView?.Invoke(EViewAction.RoutingRuleSettingWindow, item) == true)
        {
            await RefreshRoutingItems();
            IsModified = true;
        }
    }

    public async Task RoutingAdvancedRemoveAsync()
    {
        if (SelectedSource is null || SelectedSource.Remarks.IsNullOrEmpty())
        {
            NoticeHandler.Instance.Enqueue(ResUI.PleaseSelectRules);
            return;
        }
        if (await _updateView?.Invoke(EViewAction.ShowYesNo, null) == false)
        {
            return;
        }
        foreach (var it in SelectedSources ?? [SelectedSource])
        {
            var item = await AppHandler.Instance.GetRoutingItem(it?.Id);
            if (item != null)
            {
                await ConfigHandler.RemoveRoutingItem(item);
            }
        }

        await RefreshRoutingItems();
        IsModified = true;
    }

    public async Task RoutingAdvancedSetDefault()
    {
        var item = await AppHandler.Instance.GetRoutingItem(SelectedSource?.Id);
        if (item is null)
        {
            NoticeHandler.Instance.Enqueue(ResUI.PleaseSelectRules);
            return;
        }

        if (await ConfigHandler.SetDefaultRouting(_config, item) == 0)
        {
            await RefreshRoutingItems();
            IsModified = true;
        }
    }

    private async Task RoutingAdvancedImportRules()
    {
        if (await ConfigHandler.InitRouting(_config, true) == 0)
        {
            await RefreshRoutingItems();
            IsModified = true;
        }
    }
}
