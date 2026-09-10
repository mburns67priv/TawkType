using Murmur.Core.Settings;

namespace Murmur.Core.Abstractions;

public interface ISettingsProvider
{
    MurmurSettings Current { get; }

    event EventHandler? Changed;
}
