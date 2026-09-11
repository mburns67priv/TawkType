using System.Windows.Media;
using TawkType.Desktop.Views;

namespace TawkType.Desktop.ViewModels;

/// <summary>One row in the settings nav rail, and the source for the matching card on the home page.</summary>
/// <param name="Page">Which page this selects.</param>
/// <param name="Title">Nav label and card heading.</param>
/// <param name="Blurb">One-line description, shown on the home card only.</param>
public sealed record NavPage(SettingsPage Page, string Title, string Blurb, Geometry Icon)
{
    public static IReadOnlyList<NavPage> All { get; } =
    [
        new(SettingsPage.General, "General", "Overview and your stats", Icons.Home),
        new(SettingsPage.Transcription, "Transcription", "Engine, language and models", Icons.Microphone),
        new(SettingsPage.Activation, "Activation", "Hotkey and how text is inserted", Icons.Keyboard),
        new(SettingsPage.Modes, "Modes", "How the next dictation should behave", Icons.Modes),
        new(SettingsPage.Appearance, "Appearance", "Theme and the status pill", Icons.Palette),
        new(SettingsPage.Vocabulary, "Vocabulary", "Spellings, replacements and snippets", Icons.Clipboard),
        new(SettingsPage.Cleanup, "AI cleanup", "Rewrite dictations with Claude", Icons.Sparkle),
        new(SettingsPage.History, "History", "Review previous dictations", Icons.Clock),
    ];

    /// <summary>The four the home page offers as cards. General is the page you are already on.</summary>
    public static IReadOnlyList<NavPage> HomeCards { get; } =
        All.Where(page => page.Page != SettingsPage.General).ToArray();
}
