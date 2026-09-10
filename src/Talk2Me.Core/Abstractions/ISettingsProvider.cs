using Talk2Me.Core.Settings;

namespace Talk2Me.Core.Abstractions;

public interface ISettingsProvider
{
    Talk2MeSettings Current { get; }

    event EventHandler? Changed;
}
