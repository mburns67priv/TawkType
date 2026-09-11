using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using TawkType.Core.Abstractions;
using TawkType.Core.Settings;
using TawkType.Windows.Shell;

namespace TawkType.Desktop.Services;

/// <summary>
/// Swaps the theme dictionary in slot 0 of Application.Resources. Everything that varies between themes
/// is a DynamicResource, so open windows restyle themselves without being reloaded.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private readonly ISettingsProvider _settings;
    private AppTheme _applied = (AppTheme)(-1);

    /// <summary>
    /// Whether the dark dictionary is in force. Static because the theme is genuinely one global
    /// thing — it lives in Application.Resources — and windows need to ask before they have anything
    /// else to ask. A window reads this as it is created; open ones are repainted by Apply.
    /// </summary>
    public static bool IsDark { get; private set; }

    public ThemeManager(ISettingsProvider settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;

        // Only relevant while the user is on System; harmless to stay subscribed either way.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    /// <summary>Applies the configured theme. Cheap and idempotent: a no-op when nothing changed.</summary>
    public void Apply()
    {
        var wanted = _settings.Current.Appearance.Theme;
        var resolved = wanted == AppTheme.System ? DetectWindowsTheme() : wanted;

        if (resolved == _applied)
        {
            return;
        }

        _applied = resolved;

        var source = new Uri(
            resolved == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml",
            UriKind.Relative);

        var merged = Application.Current.Resources.MergedDictionaries;
        merged[0] = new ResourceDictionary { Source = source };

        // The caption bar is not ours to style through XAML; DWM owns it, and it has to be told.
        IsDark = resolved == AppTheme.Dark;
        foreach (Window window in Application.Current.Windows)
        {
            TitleBarTheme.Apply(new WindowInteropHelper(window).Handle, IsDark);
        }
    }

    /// <summary>Reads the Windows "app mode" setting. Defaults to light when the value is missing.</summary>
    public static AppTheme DetectWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightThemeValue) is int value && value == 0
                ? AppTheme.Dark
                : AppTheme.Light;
        }
        catch
        {
            // Locked-down or unusual profiles: light is the safer guess for a desktop app.
            return AppTheme.Light;
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(Apply);

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Application.Current?.Dispatcher.BeginInvoke(Apply);
        }
    }
}
