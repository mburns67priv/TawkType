using System.Windows;
using Microsoft.Win32;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Settings;

namespace Talk2Me.Desktop.Services;

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
