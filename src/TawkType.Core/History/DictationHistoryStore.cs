using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Settings;

namespace TawkType.Core.History;

/// <summary>
/// The dictation log, kept as JSON Lines in the app data folder: one record per line, appended as each
/// dictation completes. A corrupt or half-written line is skipped rather than losing the file.
///
/// Retention is about the file, not the list. Trimming what is in memory is invisible to anyone
/// worried about what is on their disk, and counting from memory is what let the file grow without
/// limit: loading trimmed the count back under the threshold, so a session could never reach it.
/// <see cref="_linesOnDisk"/> tracks the file instead, and compaction rewrites through a temporary
/// file, so a crash part-way through cannot destroy a log that was intact a moment earlier.
/// </summary>
public sealed class DictationHistoryStore : IDictationHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Compaction rewrites the file, so leave headroom rather than doing it on every add.</summary>
    private const int TrimSlack = 50;

    private readonly ISettingsProvider _settings;
    private readonly ILogger<DictationHistoryStore> _logger;
    private readonly object _gate = new();

    /// <summary>Newest first. May hold fewer records than the file does; see the note above.</summary>
    private readonly List<DictationRecord> _records = new();

    /// <summary>Lines believed to be in the file, including any too old to be kept in memory.</summary>
    private long _linesOnDisk;

    public DictationHistoryStore(ISettingsProvider settings, ILogger<DictationHistoryStore> logger)
        : this(settings, logger, System.IO.Path.Combine(SettingsStore.AppDataDirectory, "history.jsonl"))
    {
    }

    public DictationHistoryStore(ISettingsProvider settings, ILogger<DictationHistoryStore> logger, string path)
    {
        _settings = settings;
        _logger = logger;
        Path = path;
        Load();
    }

    public string Path { get; }

    public IReadOnlyList<DictationRecord> Recent
    {
        get
        {
            lock (_gate)
            {
                // Compaction is deliberately lazy, so never hand out more than the user asked to keep.
                return _records.Take(MaxEntries).ToArray();
            }
        }
    }

    public DictationRecord? Last
    {
        get
        {
            lock (_gate)
            {
                return _records.Count > 0 ? _records[0] : null;
            }
        }
    }

    public event EventHandler? Changed;

    public void Add(DictationRecord record)
    {
        if (!_settings.Current.History.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            _records.Insert(0, record);

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                TerminateLastLine();
                File.AppendAllText(Path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
                _linesOnDisk++;

                Compact();
            }
            catch (Exception ex)
            {
                // The dictation itself already worked; failing to log it must not surface as an error.
                _logger.LogWarning(ex, "Could not write the dictation history to {Path}", Path);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Clear()
    {
        bool deleted;

        lock (_gate)
        {
            // Delete first. Emptying the list before knowing the file has gone shows the user an empty
            // history that will be full again on the next launch.
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }

                File.Delete(TempPath); // a compaction that never finished
                deleted = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete the dictation history at {Path}", Path);
                deleted = false;
            }

            if (deleted)
            {
                _records.Clear();
                _linesOnDisk = 0;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return deleted;
    }

    private int MaxEntries => Math.Max(1, _settings.Current.History.MaxEntries);

    private string TempPath => Path + ".compacting";

    /// <summary>
    /// Rewrites the file down to what the user asked to keep, once it has drifted far enough past that
    /// for the write to be worth it. Called after every append and after loading, because the file can
    /// already be over the limit before this process appends anything.
    /// </summary>
    public bool Remove(string id)
    {
        bool written;

        lock (_gate)
        {
            var at = _records.FindIndex(record => record.Id == id);
            if (at < 0)
            {
                return true;
            }

            var removed = _records[at];
            _records.RemoveAt(at);

            written = RewriteFile();
            if (!written)
            {
                // Put it back. A list that no longer shows an entry still on disk is the lie that
                // Clear() exists to avoid telling.
                _records.Insert(at, removed);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return written;
    }

    public bool Replace(DictationRecord record)
    {
        bool written;

        lock (_gate)
        {
            var at = _records.FindIndex(existing => existing.Id == record.Id);
            if (at < 0)
            {
                return true;
            }

            var previous = _records[at];
            _records[at] = record;

            written = RewriteFile();
            if (!written)
            {
                _records[at] = previous;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return written;
    }

    /// <summary>
    /// Writes the whole log back out from memory. Editing or deleting one entry means rewriting the
    /// file, since JSON Lines has no way to change a line in place — which is the price of a format
    /// whose append path is one call and whose torn lines cost one record instead of the file.
    ///
    /// Through a temporary file and a move, for the same reason <see cref="Compact"/> is.
    /// </summary>
    private bool RewriteFile()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

            var lines = _records
                .AsEnumerable()
                .Reverse()
                .Select(record => JsonSerializer.Serialize(record, JsonOptions));

            File.WriteAllLines(TempPath, lines);
            File.Move(TempPath, Path, overwrite: true);
            _linesOnDisk = _records.Count;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not rewrite the dictation history at {Path}", Path);
            return false;
        }
    }

    private void Compact()
    {
        var max = MaxEntries;
        if (_linesOnDisk <= max + TrimSlack)
        {
            return;
        }

        if (_records.Count > max)
        {
            _records.RemoveRange(max, _records.Count - max);
        }

        // Through a temporary file: writing in place truncates first, so a crash half way would leave
        // a log that was whole a moment earlier in pieces.
        var lines = _records
            .AsEnumerable()
            .Reverse()
            .Select(record => JsonSerializer.Serialize(record, JsonOptions));

        File.WriteAllLines(TempPath, lines);
        File.Move(TempPath, Path, overwrite: true);
        _linesOnDisk = _records.Count;
    }

    /// <summary>
    /// Makes sure the file ends with a line break before appending. A write torn by a crash or a full
    /// disk leaves a partial line with no newline, and appending to that glues the next record onto the
    /// wreckage — losing the new dictation as well as the old one.
    /// </summary>
    private void TerminateLastLine()
    {
        var file = new FileInfo(Path);
        if (!file.Exists || file.Length == 0)
        {
            return;
        }

        using var stream = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Seek(-1, SeekOrigin.End);

        var last = stream.ReadByte();
        if (last == '\n' || last == '\r')
        {
            return;
        }

        _logger.LogWarning("The dictation history ended mid-line; closing it before appending");
        stream.Seek(0, SeekOrigin.End);
        var terminator = Encoding.UTF8.GetBytes(Environment.NewLine);
        stream.Write(terminator, 0, terminator.Length);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            foreach (var line in File.ReadLines(Path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                _linesOnDisk++;

                try
                {
                    if (JsonSerializer.Deserialize<DictationRecord>(line, JsonOptions) is { } record)
                    {
                        _records.Insert(0, record);
                    }
                }
                catch (JsonException)
                {
                    // A line torn by a crash mid-write. Skip it; the rest of the log is still good.
                }
            }

            // Bring the file back within the limit, not just the list. Without this it grows for ever
            // while the window faithfully shows the newest few hundred.
            Compact();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the dictation history from {Path}; starting empty", Path);
            _records.Clear();
            _linesOnDisk = 0;
        }
    }
}
