using TawkType.Core.Abstractions;
using TawkType.Core.Models;

namespace TawkType.Core.History;

/// <summary>
/// The most recent dictation, held in memory for as long as TawkType runs.
///
/// Separate from the history on purpose. History is a file the user can switch off or delete, and
/// someone who wants no record on disk still wants the Copy button to work for the thing they just
/// said. This never touches the disk, so it honours that choice and stays useful.
///
/// It is set when the words are recognised, before delivery is attempted, so a dictation that fails
/// to arrive is still here to be copied.
/// </summary>
public sealed class LastDictation
{
    private readonly object _gate = new();
    private DictationRecord? _value;

    /// <summary>Seeds from the history so the button works on the first dictation after a restart.</summary>
    public LastDictation(IDictationHistory history)
    {
        _value = history.Last;
    }

    public DictationRecord? Value
    {
        get
        {
            lock (_gate)
            {
                return _value;
            }
        }
    }

    public event EventHandler? Changed;

    public void Set(DictationRecord record)
    {
        lock (_gate)
        {
            _value = record;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
