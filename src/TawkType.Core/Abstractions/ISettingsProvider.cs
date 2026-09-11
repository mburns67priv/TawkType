using TawkType.Core.Settings;

namespace TawkType.Core.Abstractions;

public interface ISettingsProvider
{
    TawkTypeSettings Current { get; }

    event EventHandler? Changed;
}
