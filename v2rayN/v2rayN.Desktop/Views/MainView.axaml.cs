using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using DialogHostAvalonia;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Recyclable content of <see cref="MainWindow"/>. It is detached and discarded when the window is
/// hidden to the tray, and rebuilt from <see cref="MainWindowViewModel"/> when the window is shown again.
/// </summary>
public partial class MainView : ReactiveUserControl<MainWindowViewModel>
{
    private static Config _config;
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;

    private MsgView? _msgView;
    private ProfilesView? _profilesView;
    private ClashConnectionsView? _clashConnectionsView;
    private ClashProxiesView? _clashProxiesView;

    public MainView()
    {
        InitializeComponent();

        _config = ConfigTextHandler.Config;

        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        btnNewUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuClose.Click += MenuClose_Click;

        conTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddMasqueServerCmd, v => v.menuAddMasqueServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomOutboundServerCmd, v => v.menuAddCustomOutboundServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.IsVisible).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel, v => v.contentStatusBarView.Content).DisposeWith(disposables);

            var layoutBindings = new SingleReplaceableDisposable().DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(orientation =>
                {
                    UpdateLayout(orientation, layoutBindings);
                })
                .DisposeWith(disposables);
        });

        if (!Utils.IsWindows())
        {
            menuAddServerViaScan.IsVisible = false;
        }

        AddHelpMenuItem();
    }

    /// <summary>
    /// Persists the visible view state. Must be called while the view is still attached, because the
    /// view is discarded once the window is hidden to the tray.
    /// </summary>
    public void StorageUI()
    {
        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal
            && gridMain.ColumnDefinitions.Count > 2)
        {
            ConfigTextHandler.SaveMainGirdHeight(gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical
            && gridMain.RowDefinitions.Count > 2)
        {
            ConfigTextHandler.SaveMainGirdHeight(gridMain.RowDefinitions[0].ActualHeight, gridMain.RowDefinitions[2].ActualHeight);
        }

        _profilesView?.StorageUI();
        _clashConnectionsView?.StorageUI();
    }

    #region Event

    private void MenuPromotion_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private void MenuSettingsSetUWP_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    private void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        DialogHost.Show(_checkUpdateView);

        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView);
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            await mainWindow.CloseFromMenuAsync();
        }
    }

    #endregion Event

    #region UI

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
    }

    private void UpdateLayout(EGirdOrientation orientation, SingleReplaceableDisposable disposable)
    {
        var currentLayoutDisposables = new MultipleDisposable();
        disposable.Create(currentLayoutDisposables);
        gridMain.IsVisible = true;
        _msgView ??= new MsgView
        {
            DataContext = ViewModel?.MsgViewModel,
        };
        _profilesView ??= new ProfilesView
        {
            DataContext = ViewModel?.ProfilesViewModel,
        };
        _clashProxiesView ??= new ClashProxiesView
        {
            DataContext = ViewModel?.ClashProxiesViewModel,
        };
        _clashConnectionsView ??= new ClashConnectionsView
        {
            DataContext = ViewModel?.ClashConnectionsViewModel,
        };

        switch (orientation)
        {
            case EGirdOrientation.Horizontal:
            default:
                gridMain.ColumnDefinitions = new ColumnDefinitions("1*,10,1*");
                gridMain.RowDefinitions = new RowDefinitions("1*");
                Grid.SetColumn(tabProfiles, 0);
                Grid.SetColumn(gridSplitter, 1);
                Grid.SetColumn(tabDockPanel, 2);
                Grid.SetRow(tabProfiles, 0);
                Grid.SetRow(gridSplitter, 0);
                Grid.SetRow(tabDockPanel, 0);
                tabStripH.IsVisible = true;
                tabStripV.IsVisible = false;
                gridSplitter.IsVisible = true;
                gridSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                tabContent.Content = null;
                tabProfiles.Content = _profilesView;
                this.WhenAnyValue(x => x.ViewModel!.ShowClashUI)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(show =>
                    {
                        if (show)
                        {
                            tabStripH.ItemsSource = new List<string>
                            {
                                ResUI.MsgInformationTitle,
                                ResUI.TbProxies,
                                ResUI.TbConnections,
                            };
                            tabStripH.IsVisible = true;
                        }
                        else
                        {
                            tabStripH.IsVisible = false;
                            tabStripH.ItemsSource = new List<string>();
                        }
                    }).DisposeWith(currentLayoutDisposables);
                tabStripH.GetObservable(SelectingItemsControl.SelectedIndexProperty)
                    .Subscribe(index =>
                    {
                        tabContent.Content = index switch
                        {
                            1 => _clashProxiesView,
                            2 => _clashConnectionsView,
                            _ => _msgView,
                        };
                    }).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Vertical:
                gridMain.RowDefinitions = new RowDefinitions("1*,10,1*");
                gridMain.ColumnDefinitions = new ColumnDefinitions("1*");
                Grid.SetRow(tabProfiles, 0);
                Grid.SetRow(gridSplitter, 1);
                Grid.SetRow(tabDockPanel, 2);
                Grid.SetColumn(tabProfiles, 0);
                Grid.SetColumn(gridSplitter, 0);
                Grid.SetColumn(tabDockPanel, 0);
                tabStripV.IsVisible = true;
                tabStripH.IsVisible = false;
                gridSplitter.IsVisible = true;
                gridSplitter.VerticalAlignment = VerticalAlignment.Stretch;
                tabContent.Content = null;
                tabProfiles.Content = _profilesView;
                this.WhenAnyValue(x => x.ViewModel!.ShowClashUI)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(show =>
                    {
                        if (show)
                        {
                            tabStripV.ItemsSource = new List<string>
                            {
                                ResUI.MsgInformationTitle,
                                ResUI.TbProxies,
                                ResUI.TbConnections,
                            };
                            tabStripV.IsVisible = true;
                        }
                        else
                        {
                            tabStripV.IsVisible = false;
                            tabStripV.ItemsSource = new List<string>();
                        }
                    }).DisposeWith(currentLayoutDisposables);
                tabStripV.GetObservable(SelectingItemsControl.SelectedIndexProperty)
                    .Subscribe(index =>
                    {
                        tabContent.Content = index switch
                        {
                            1 => _clashProxiesView,
                            2 => _clashConnectionsView,
                            _ => _msgView,
                        };
                    }).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Tab:
                gridMain.RowDefinitions = new RowDefinitions("1*");
                gridMain.ColumnDefinitions = new ColumnDefinitions("1*");
                Grid.SetColumn(tabProfiles, 0);
                Grid.SetColumn(gridSplitter, 0);
                Grid.SetColumn(tabDockPanel, 0);
                Grid.SetRow(tabProfiles, 0);
                Grid.SetRow(gridSplitter, 0);
                Grid.SetRow(tabDockPanel, 0);
                tabStripV.IsVisible = true;
                tabStripH.IsVisible = false;
                gridSplitter.IsVisible = false;
                tabContent.Content = null;
                tabProfiles.Content = null;
                this.WhenAnyValue(x => x.ViewModel!.ShowClashUI)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(show =>
                    {
                        if (show)
                        {
                            tabStripV.ItemsSource = new List<string>
                            {
                                ResUI.menuServers,
                                ResUI.MsgInformationTitle,
                                ResUI.TbProxies,
                                ResUI.TbConnections,
                            };
                            tabStripV.IsVisible = true;
                        }
                        else
                        {
                            tabStripV.ItemsSource = new List<string>
                            {
                                ResUI.menuServers,
                                ResUI.MsgInformationTitle,
                            };
                            tabStripV.IsVisible = true;
                        }
                    }).DisposeWith(currentLayoutDisposables);
                tabStripV.GetObservable(SelectingItemsControl.SelectedIndexProperty)
                    .Subscribe(index =>
                    {
                        tabContent.Content = index switch
                        {
                            1 => _msgView,
                            2 => _clashProxiesView,
                            3 => _clashConnectionsView,
                            _ => _profilesView,
                        };
                    }).DisposeWith(currentLayoutDisposables);
                break;
        }

        RestoreUI();
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType is not ECoreType.v2fly
                        and not ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url?.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
    }

    private void MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag?.ToString());
        }
    }

    #endregion UI
}
