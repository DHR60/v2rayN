using Semi.Avalonia;

namespace v2rayN.Desktop.Common;

internal static class AppStyles
{
    public static void ApplyTheme(string? currentTheme)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }
        app.RequestedThemeVariant = currentTheme switch
        {
            nameof(ETheme.Dark) => ThemeVariant.Dark,
            nameof(ETheme.Light) => ThemeVariant.Light,
            nameof(ETheme.Aquatic) => SemiTheme.Aquatic,
            nameof(ETheme.Desert) => SemiTheme.Desert,
            nameof(ETheme.Dusk) => SemiTheme.Dusk,
            nameof(ETheme.NightSky) => SemiTheme.NightSky,
            _ => ThemeVariant.Default,
        };
    }

    public static void ApplyFontSize(double size)
    {
        if (size < Global.MinFontSize || Application.Current is null)
        {
            return;
        }
        Application.Current.Resources["AppFontSize"] = size;
        ApplyDataGridRowHeight(size);
    }

    public static void ApplyDataGridRowHeight(double size)
    {
        var rowHeight = 20 + (size / 2);
        if (Application.Current is not null)
        {
            Application.Current.Resources["DataGridRowHeight"] = rowHeight;
        }
    }

    public static void ApplyFontFamily(string? currentFontFamily)
    {
        if (currentFontFamily.IsNullOrEmpty() || Application.Current is null)
        {
            return;
        }

        try
        {
            Application.Current.Resources["AppFontFamily"] = new FontFamily(currentFontFamily);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ApplyFontFamily", ex);
        }
    }
}
