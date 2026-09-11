using System.Windows.Automation;
using System.Windows.Controls;
using TawkType.Core.Settings;

namespace TawkType.Desktop.ViewModels;

/// <summary>
/// What a language combo box shows: detect, then the five most spoken, a separator, then the rest A
/// to Z. Mixed types because a WPF ComboBox takes a Separator as an item and draws it as one.
///
/// A fresh list every call, deliberately: the separator is a real control and one instance cannot be
/// in two combo boxes at once. Settings and first run each get their own.
/// </summary>
internal static class LanguagePicker
{
    public static IReadOnlyList<object> Options()
    {
        var options = new List<object> { Languages.Auto };
        options.AddRange(Languages.MostSpoken);

        // Disabled so it cannot be landed on with the keyboard or picked by accident, and unnamed so a
        // screen reader does not announce the type name of a decoration.
        var divider = new Separator { IsEnabled = false };
        AutomationProperties.SetName(divider, string.Empty);
        options.Add(divider);

        options.AddRange(Languages.Rest);
        return options;
    }
}
