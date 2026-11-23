using Avalonia.Controls.Notifications;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    /// <summary>
    /// How long the window content stays alive after being hidden to the tray before it is released.
    /// </summary>
    private static readonly TimeSpan MainViewReleaseDelay = TimeSpan.FromSeconds(10);

    private static Config _config;
    private readonly WindowNotificationManager? _manager;
    private readonly MultipleDisposable _viewSubscriptions = new();
    private IDisposable? _mainViewReleaseTimer;
    private bool _blCloseByUser = false;
    private bool _blInteractionsRegistered = false;

    public MainWindow()
    {
        InitializeComponent();

        _config = ConfigTextHandler.Config;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3, Position = NotificationPosition.TopRight };

        KeyDown += MainWindow_KeyDown;
        DataContextChanged += (_, _) => RegisterInteractions();

        UpdateTitle();

        if (Utils.IsWindows() && !Design.IsDesignMode)
        {
            ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
            HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
        }

        if (_config.UiItem.AutoHideStartup && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }
    }

    #region View lifecycle

    /// <summary>
    /// Rebuilds the recyclable content from <see cref="MainWindowViewModel"/>. Used after view related
    /// settings changed so that the new resources/culture are picked up without restarting the app.
    /// </summary>
    public void RebuildMainView()
    {
        var wasVisible = IsVisible;
        UpdateTitle();
        ReleaseMainView(true);
        if (wasVisible)
        {
            EnsureMainView();
        }
    }

    /// <summary>
    /// Creates the window content if it was released while hidden to the tray.
    /// </summary>
    public void EnsureMainView()
    {
        RegisterInteractions();

        if (contentMain.Content is not null || ViewModel is null)
        {
            return;
        }

        contentMain.Content = new MainView
        {
            ViewModel = ViewModel,
        };
    }

    private void ReleaseMainView(bool force = false)
    {
        CancelMainViewRelease();

        if (!force && IsVisible)
        {
            return;
        }

        if (contentMain.Content is MainView mainView)
        {
            mainView.StorageUI();
        }
        contentMain.Content = null;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void ScheduleMainViewRelease()
    {
        CancelMainViewRelease();
        _mainViewReleaseTimer = DispatcherTimer.RunOnce(() => ReleaseMainView(),
            MainViewReleaseDelay,
            DispatcherPriority.Background);
    }

    private void CancelMainViewRelease()
    {
        _mainViewReleaseTimer?.Dispose();
        _mainViewReleaseTimer = null;
    }

    #endregion View lifecycle

    #region Init

    private void RegisterInteractions()
    {
        if (_blInteractionsRegistered || ViewModel is null)
        {
            return;
        }
        _blInteractionsRegistered = true;

        ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
        {
            var result = await AvaUtils.GetClipboardData(this);
            interaction.SetOutput(result);
        }).DisposeWith(_viewSubscriptions);

        ViewModel.ScanScreenInteraction.RegisterHandler(async interaction =>
        {
            ShowHideWindow(false);
            await Task.Delay(200);
            var result = QRCodeAvaloniaUtils.CaptureScreen();
            ShowHideWindow(true);
            interaction.SetOutput(result);
        }).DisposeWith(_viewSubscriptions);

        ViewModel.BrowseImageFileInteraction.RegisterHandler(async interaction =>
        {
            var result = await UI.OpenFileDialog(null);
            interaction.SetOutput(result);
        }).DisposeWith(_viewSubscriptions);

        ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
        {
            ShowHideWindow(interaction.Input);
            interaction.SetOutput(RxVoid.Default);
        }).DisposeWith(_viewSubscriptions);

        // Tray features must keep working while the window content is released.
        StatusBarViewModel.Instance.SetClipboardDataInteraction.RegisterHandler(async interaction =>
        {
            await AvaUtils.SetClipboardData(this, interaction.Input);
            interaction.SetOutput(RxVoid.Default);
        }).DisposeWith(_viewSubscriptions);

        StatusBarViewModel.Instance.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
        {
            Dispatcher.UIThread.Post(RefreshIcon, DispatcherPriority.Default);
            interaction.SetOutput(RxVoid.Default);
        }).DisposeWith(_viewSubscriptions);

        AppEvents.SendSnackMsgRequested
          .AsObservable()
          .ObserveOn(RxSchedulers.MainThreadScheduler)
          .Subscribe(async content => await DelegateSnackMsg(content))
          .DisposeWith(_viewSubscriptions);

        AppEvents.AppExitRequested
          .AsObservable()
          .ObserveOn(RxSchedulers.MainThreadScheduler)
          .Subscribe(_ => StorageUI())
          .DisposeWith(_viewSubscriptions);

        AppEvents.ShutdownRequested
          .AsObservable()
          .ObserveOn(RxSchedulers.MainThreadScheduler)
          .Subscribe(Shutdown)
          .DisposeWith(_viewSubscriptions);

        AppEvents.RebuildViewRequested
          .AsObservable()
          .ObserveOn(RxSchedulers.MainThreadScheduler)
          .Subscribe(_ => RebuildMainView())
          .DisposeWith(_viewSubscriptions);
    }

    private void UpdateTitle()
    {
        Title = Utils.IsWindows()
            ? $"{Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}"
            : $"{Utils.GetVersion()}";
    }

    private void RefreshIcon()
    {
        Icon = AvaUtils.GetAppIcon(_config.SystemProxyItem.SysProxyType);
        var iconslist = TrayIcon.GetIcons(Application.Current);
        if (iconslist is { Count: > 0 })
        {
            iconslist[0].Icon = Icon;
            TrayIcon.SetIcons(Application.Current, iconslist);
        }
    }

    private async Task DelegateSnackMsg(string content)
    {
        _manager?.Show(new Avalonia.Controls.Notifications.Notification(null, content, NotificationType.Information));
        await Task.CompletedTask;
    }

    private void OnProgramStarted(object state, bool timeout)
    {
        Dispatcher.UIThread.Post(() =>
                ShowHideWindow(true),
            DispatcherPriority.Default);
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                Dispatcher.UIThread.Post(() => ShowHideWindow(null));
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    #endregion Init

    #region Event

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        Logging.SaveLog("OnClosing -> " + e.CloseReason.ToString());

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelMainViewRelease();
        _viewSubscriptions.Dispose();
        base.OnClosed(e);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.V:
                    await AddServerViaClipboardAsync();
                    break;

                case Key.S:
                    await ScanScreenTaskAsync();
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = await AvaUtils.GetClipboardData(this);
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    public async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        await Task.Delay(200);

        var bytes = QRCodeAvaloniaUtils.CaptureScreen();
        if (bytes != null && ViewModel != null)
        {
            await ViewModel.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    public async Task CloseFromMenuAsync()
    {
        try
        {
            if (await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
            {
                return;
            }

            _blCloseByUser = true;
            StorageUI();

            await AppManager.Instance.AppExitAsync(true);
        }
        catch
        {
            // Ignore
        }
    }

    private void Shutdown(bool obj)
    {
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }
        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            HotkeyManager.Instance.Dispose();
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ??
                    (Utils.IsLinux() || Utils.IsMacOS()
                    ? (!AppManager.Instance.ShowInTaskbar ^ (WindowState == WindowState.Minimized))
                    : !AppManager.Instance.ShowInTaskbar);
        if (bl)
        {
            CancelMainViewRelease();
            EnsureMainView();
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }
        else
        {
            if (Utils.IsLinux() && _config.UiItem.Hide2TrayWhenClose == false)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            StorageUI();
            foreach (var ownedWindow in OwnedWindows)
            {
                ownedWindow.Close();
            }
            Hide();
            ScheduleMainViewRelease();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
    }

    private void StorageUI()
    {
        ConfigTextHandler.SaveWindowSizeItem(GetType().Name, Width, Height);

        if (contentMain.Content is MainView mainView)
        {
            mainView.StorageUI();
        }
    }

    #endregion UI
}
