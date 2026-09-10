using Talk2Me.Core.Models;

namespace Talk2Me.Core.Abstractions;

/// <summary>The log of past dictations, newest first.</summary>
public interface IDictationHistory
{
    /// <summary>Where the log is kept, so the UI can point the user at it.</summary>
    string Path { get; }

    /// <summary>Newest first, capped at the configured maximum.</summary>
    IReadOnlyList<DictationRecord> Recent { get; }

    /// <summary>The last thing typed, or null when nothing has been dictated yet.</summary>
    DictationRecord? Last { get; }

    /// <summary>Records a dictation. A no-op when history is switched off in settings.</summary>
    void Add(DictationRecord record);

    /// <summary>Forgets everything, on disk as well as in memory.</summary>
    void Clear();

    /// <summary>Raised after <see cref="Add"/> or <see cref="Clear"/>. May arrive on a background thread.</summary>
    event EventHandler? Changed;
}
