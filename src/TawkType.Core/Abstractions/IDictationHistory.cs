using TawkType.Core.Models;

namespace TawkType.Core.Abstractions;

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

    /// <summary>
    /// Forgets everything, on disk as well as in memory. Returns false when the file could not be
    /// deleted and the log will therefore come back on the next launch — an empty list is not proof
    /// that anything was erased, and the user has to be told the difference.
    /// </summary>
    bool Clear();

    /// <summary>
    /// Forgets one dictation. Returns false when the file could not be rewritten, in which case the
    /// entry is still on disk — the same honesty <see cref="Clear"/> owes the user, for the same
    /// reason.
    /// </summary>
    bool Remove(string id);

    /// <summary>
    /// Replaces a record with a corrected version of itself, matched on id. Returns false when the
    /// file could not be rewritten. Used when the user edits a past transcript, or re-runs cleanup
    /// over it.
    /// </summary>
    bool Replace(DictationRecord record);

    /// <summary>Raised after <see cref="Add"/>, <see cref="Clear"/>, <see cref="Remove"/> or <see cref="Replace"/>. May arrive on a background thread.</summary>
    event EventHandler? Changed;
}
