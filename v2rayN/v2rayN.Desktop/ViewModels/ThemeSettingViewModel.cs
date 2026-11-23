using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.ViewModels;

public partial class ThemeSettingViewModel : MyReactiveObject
{
    [Reactive] public partial string CurrentTheme { get; set; }

    [Reactive] public partial int CurrentFontSize { get; set; }

    [Reactive] public partial string CurrentLanguage { get; set; }

    public ThemeSettingViewModel()
    {
        _config = ConfigTextHandler.Config;

        BindingUI();
        RestoreUI();
    }

    private void RestoreUI()
    {
        AppStyles.ApplyTheme(_config.UiItem.CurrentTheme);
        AppStyles.ApplyFontFamily(_config.UiItem.CurrentFontFamily);
        AppStyles.ApplyFontSize(_config.UiItem.CurrentFontSize);
    }

    private void BindingUI()
    {
        CurrentTheme = _config.UiItem.CurrentTheme;
        CurrentFontSize = _config.UiItem.CurrentFontSize;
        CurrentLanguage = _config.UiItem.CurrentLanguage;

        // Set Default Value
        AppStyles.ApplyDataGridRowHeight(12);

        this.WhenAnyValue(x => x.CurrentTheme)
            .Where(y => y.IsNotEmpty())
            .SubscribeAsync(async _ =>
            {
                if (_config.UiItem.CurrentTheme != CurrentTheme)
                {
                    _config.UiItem.CurrentTheme = CurrentTheme;
                    AppStyles.ApplyTheme(CurrentTheme);
                    await ConfigTextHandler.SaveConfig();
                }
            });

        this.WhenAnyValue(x => x.CurrentFontSize)
            .Where(y => y > 0)
            .SubscribeAsync(async _ =>
            {
                if (_config.UiItem.CurrentFontSize != CurrentFontSize && CurrentFontSize >= Global.MinFontSize)
                {
                    _config.UiItem.CurrentFontSize = CurrentFontSize;
                    AppStyles.ApplyFontSize(CurrentFontSize);
                    await ConfigTextHandler.SaveConfig();
                }
            });

        this.WhenAnyValue(x => x.CurrentLanguage)
            .Where(y => !y.IsNullOrEmpty())
            .SubscribeAsync(async _ =>
            {
                if (CurrentLanguage.IsNotEmpty() && _config.UiItem.CurrentLanguage != CurrentLanguage)
                {
                    _config.UiItem.CurrentLanguage = CurrentLanguage;
                    var culture = new CultureInfo(CurrentLanguage);
                    Thread.CurrentThread.CurrentUICulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                    await ConfigTextHandler.SaveConfig();
                    AppEvents.RebuildViewRequested.Publish();
                }
            });
    }
}
