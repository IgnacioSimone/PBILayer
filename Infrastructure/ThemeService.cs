using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PBIExplorer.Infrastructure;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>Cambia la paleta en caliente: los pinceles de App.xaml son instancias
/// compartidas por todas las vistas (StaticResource), así que alcanza con mutar su
/// Color — no hace falta recargar ventanas ni usar DynamicResource.</summary>
public static class ThemeService
{
    private static readonly Dictionary<string, string> Light = new()
    {
        ["WindowBg"] = "#F5F5F2", ["SidebarBg"] = "#ECECE8", ["CardBg"] = "#FFFFFF",
        ["Separator"] = "#DCDCD6", ["SeparatorSoft"] = "#EDEDE8",
        ["Ink"] = "#151616", ["InkDim"] = "#565753", ["InkFaint"] = "#6A6B66",
        ["Accent"] = "#8A6400", ["AccentSoft"] = "#FFF3C4",
        ["RowAlt"] = "#FAFAF7", ["RowHover"] = "#FFF8DA",
        ["NavHover"] = "#E2E2DC", ["PanelTint"] = "#E6E6E0",
        ["ButtonBg"] = "#FFFFFF", ["ButtonHover"] = "#F5F5F2", ["ButtonPressed"] = "#EAEAE5",
        ["ThumbBrush"] = "#38000000", ["ThumbHoverBrush"] = "#60000000",
        ["StatusBad"] = "#C0392B", ["StatusWarn"] = "#96650F", ["StatusGood"] = "#1C7C4C",
        ["SyntaxKeyword"] = "#A12C7B", ["SyntaxFunction"] = "#0F6AA6", ["SyntaxString"] = "#1C7C4C",
        ["SyntaxNumber"] = "#96650F", ["SyntaxComment"] = "#5E725E", ["SyntaxReference"] = "#8A6400",
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["WindowBg"] = "#1F2020", ["SidebarBg"] = "#171818", ["CardBg"] = "#272828",
        ["Separator"] = "#3B3C3B", ["SeparatorSoft"] = "#313231",
        ["Ink"] = "#F5F5F2", ["InkDim"] = "#C5C5C0", ["InkFaint"] = "#A6A6A1",
        ["Accent"] = "#F6C944", ["AccentSoft"] = "#3A3317",
        ["RowAlt"] = "#2C2D2D", ["RowHover"] = "#393520",
        ["NavHover"] = "#2D2E2E", ["PanelTint"] = "#2B2C2C",
        ["ButtonBg"] = "#343535", ["ButtonHover"] = "#3E3F3F", ["ButtonPressed"] = "#2A2B2B",
        ["ThumbBrush"] = "#40FFFFFF", ["ThumbHoverBrush"] = "#70FFFFFF",
        ["StatusBad"] = "#F28B7D", ["StatusWarn"] = "#E6B85C", ["StatusGood"] = "#63C995",
        ["SyntaxKeyword"] = "#F28EC4", ["SyntaxFunction"] = "#7FC1F0", ["SyntaxString"] = "#7FD6A2",
        ["SyntaxNumber"] = "#E6B85C", ["SyntaxComment"] = "#92A692", ["SyntaxReference"] = "#F6C944",
    };

    private static ThemeMode _mode = ThemeMode.System;
    private static bool _listening;

    public static ThemeMode Mode => _mode;

    /// <summary>True si la paleta oscura está activa ahora (según el modo elegido y Windows).</summary>
    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    public static void Apply(ThemeMode mode)
    {
        _mode = mode;

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, _) =>
            {
                if (_mode == ThemeMode.System) Application.Current?.Dispatcher.Invoke(Refresh);
            };
        }

        Refresh();
    }

    public static ThemeMode Next() => _mode switch
    {
        ThemeMode.System => ThemeMode.Light,
        ThemeMode.Light => ThemeMode.Dark,
        _ => ThemeMode.System
    };

    private static void Refresh()
    {
        var app = Application.Current;
        if (app is null) return;

        var dark = _mode == ThemeMode.Dark || (_mode == ThemeMode.System && WindowsUsesDarkApps());
        IsDark = dark;

        var palette = dark ? Dark : Light;
        foreach (var (key, hex) in palette)
            SetBrush(app, key, (Color)ColorConverter.ConvertFromString(hex));

        // Los rellenos amarillos (con texto casi negro encima) no cambian entre temas.
        SetBrush(app, "AccentStrong", (Color)ColorConverter.ConvertFromString("#F6C944"));
        SetBrush(app, "OnAccent", (Color)ColorConverter.ConvertFromString("#121313"));
        SetBrush(app, "AccentHover", (Color)ColorConverter.ConvertFromString("#FFD761"));
        SetBrush(app, "AccentPressed", (Color)ColorConverter.ConvertFromString("#D9A71E"));

        // Modo de alto contraste de Windows: manda el sistema, no nuestra paleta.
        if (SystemParameters.HighContrast) ApplyHighContrast(app);

        foreach (Window window in app.Windows) UseImmersiveDarkTitleBar(window, dark);
        Changed?.Invoke();
    }

    // Los pinceles de App.xaml quedan congelados al cargarse, así que no se pueden mutar:
    // se reemplazan por pinceles nuevos y todo lo que los usa vía DynamicResource se actualiza.
    private static void SetBrush(Application app, string key, Color color) =>
        app.Resources[key] = new SolidColorBrush(color);

    private static void ApplyHighContrast(Application app)
    {
        void Set(string key, Color color) => SetBrush(app, key, color);
        Set("WindowBg", SystemColors.WindowColor);
        Set("SidebarBg", SystemColors.WindowColor);
        Set("CardBg", SystemColors.WindowColor);
        Set("RowAlt", SystemColors.WindowColor);
        Set("RowHover", SystemColors.HighlightColor);
        Set("AccentSoft", SystemColors.HighlightColor);
        Set("Separator", SystemColors.WindowTextColor);
        Set("SeparatorSoft", SystemColors.WindowTextColor);
        Set("Ink", SystemColors.WindowTextColor);
        Set("InkDim", SystemColors.WindowTextColor);
        Set("InkFaint", SystemColors.WindowTextColor);
        Set("Accent", SystemColors.HotTrackColor);
        Set("AccentStrong", SystemColors.HighlightColor);
        Set("OnAccent", SystemColors.HighlightTextColor);
        Set("NavHover", SystemColors.ControlColor);
        Set("PanelTint", SystemColors.ControlColor);
        Set("ButtonBg", SystemColors.ControlColor);
        Set("ButtonHover", SystemColors.ControlColor);
        Set("ButtonPressed", SystemColors.ControlColor);
    }

    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Barra de título oscura (Windows 10 20H1+ / 11) para que no desentone con el contenido.</summary>
    public static void UseImmersiveDarkTitleBar(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref value, sizeof(int));
        }
        catch
        {
            // Cosmético: en versiones viejas de Windows simplemente no hace nada.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
