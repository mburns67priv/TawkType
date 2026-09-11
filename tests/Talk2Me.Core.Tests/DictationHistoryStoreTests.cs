using Microsoft.Extensions.Logging.Abstractions;
using Talk2Me.Core.History;
using Talk2Me.Core.Models;
using Talk2Me.Core.Tests.Fakes;

namespace Talk2Me.Core.Tests;

public sealed class DictationHistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Talk2Me.Tests." + Guid.NewGuid().ToString("N"));
    private readonly FakeSettings _settings = new();

    public DictationHistoryStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Path_ => Path.Combine(_root, "history.jsonl");

    private DictationHistoryStore CreateStore() =>
        new(_settings, NullLogger<DictationHistoryStore>.Instance, Path_);

    private static DictationRecord Record(string final, string? raw = null) => new()
    {
        RawText = raw ?? final,
        FinalText = final,
        Engine = "Parakeet",
        AudioSeconds = 3.2,
        TranscriptionMs = 215,
    };

    [Fact]
    public void Keeps_the_newest_dictation_first()
    {
        var store = CreateStore();
        store.Add(Record("first"));
        store.Add(Record("second"));

        Assert.Equal("second", store.Last?.FinalText);
        Assert.Equal(["second", "first"], store.Recent.Select(r => r.FinalText));
    }

    [Fact]
    public void Last_is_null_before_anything_is_dictated()
    {
        Assert.Null(CreateStore().Last);
    }

    [Fact]
    public void Survives_a_restart()
    {
        var store = CreateStore();
        store.Add(Record("typed text", raw: "um typed text"));

        var reopened = CreateStore();
        var record = Assert.Single(reopened.Recent);
        Assert.Equal("typed text", record.FinalText);
        Assert.Equal("um typed text", record.RawText);
        Assert.Equal("Parakeet", record.Engine);
        Assert.Equal(215, record.TranscriptionMs);
    }

    [Fact]
    public void Records_nothing_while_history_is_switched_off()
    {
        _settings.Current.History.Enabled = false;

        var store = CreateStore();
        store.Add(Record("not logged"));

        Assert.Empty(store.Recent);
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void Clear_empties_the_log_and_the_file()
    {
        var store = CreateStore();
        store.Add(Record("something"));

        store.Clear();

        Assert.Empty(store.Recent);
        Assert.False(File.Exists(Path_));
        Assert.Empty(CreateStore().Recent);
    }

    [Fact]
    public void Trims_back_to_the_maximum_once_the_slack_runs_out()
    {
        _settings.Current.History.MaxEntries = 5;

        var store = CreateStore();
        for (var i = 0; i < 60; i++)
        {
            store.Add(Record($"entry {i}"));
        }

        Assert.Equal(5, store.Recent.Count);
        Assert.Equal("entry 59", store.Last?.FinalText);
        Assert.Equal(5, CreateStore().Recent.Count);
    }

    [Fact]
    public void Skips_a_line_torn_by_a_crash_and_keeps_the_rest()
    {
        var store = CreateStore();
        store.Add(Record("good one"));
        File.AppendAllText(Path_, "{\"FinalText\": \"half a lin" + Environment.NewLine);

        var record = Assert.Single(CreateStore().Recent);
        Assert.Equal("good one", record.FinalText);
    }

    [Fact]
    public void Reports_a_dictation_as_cleaned_only_when_the_text_changed()
    {
        Assert.True(Record("The deadline is Tuesday.", raw: "um the deadline is tuesday").WasCleaned);
        Assert.False(Record("Hello world").WasCleaned);
    }

    [Fact]
    public void Raises_changed_on_add_and_clear()
    {
        var store = CreateStore();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Add(Record("one"));
        store.Clear();

        Assert.Equal(2, raised);
    }

    /// <summary>Lines actually in the file, which is what retention is supposed to be about.</summary>
    private int LinesOnDisk() => File.Exists(Path_)
        ? File.ReadAllLines(Path_).Count(line => !string.IsNullOrWhiteSpace(line))
        : 0;

    /// <summary>
    /// The defect this was written for: retention was decided on the in-memory count, and loading
    /// trimmed that count back under the threshold. Each restart reset it, so a session could never
    /// reach compaction and the file grew for ever while the window showed the newest few.
    ///
    /// Fifteen short sessions is past the point where the old code could ever have caught up.
    /// </summary>
    [Fact]
    public void Restarting_does_not_let_the_file_grow_past_the_limit()
    {
        _settings.Current.History.MaxEntries = 10;

        for (var session = 0; session < 15; session++)
        {
            var store = CreateStore();
            for (var i = 0; i < 8; i++)
            {
                store.Add(Record($"session {session} entry {i}"));
            }
        }

        // 120 dictations, ten kept. Compaction has slack, so the file is allowed to run ahead of the
        // limit — but not by the whole history.
        Assert.True(LinesOnDisk() <= 70, $"the history file holds {LinesOnDisk()} lines");
        Assert.Equal(10, CreateStore().Recent.Count);
    }

    /// <summary>
    /// A file that arrived over the limit — from an older build, or a longer retention setting — has to
    /// be brought back on load. Trimming only the list left the words on disk.
    /// </summary>
    [Fact]
    public void Loading_a_file_that_is_already_far_too_long_compacts_it()
    {
        _settings.Current.History.MaxEntries = 5;

        var line = Serialised(Record("old entry"));
        File.WriteAllLines(Path_, Enumerable.Repeat(line, 200));

        _ = CreateStore();

        Assert.True(LinesOnDisk() <= 55, $"the history file holds {LinesOnDisk()} lines");
    }

    /// <summary>One record in the on-disk form, for building a file the store did not write.</summary>
    private string Serialised(DictationRecord record)
    {
        var store = CreateStore();
        store.Add(record);
        var line = File.ReadAllLines(Path_).Last(l => !string.IsNullOrWhiteSpace(l));
        File.Delete(Path_);
        return line;
    }

    /// <summary>
    /// A crash mid-write leaves a line with no newline. Appending to that glued the next record onto
    /// the wreckage, so one torn write cost two dictations.
    /// </summary>
    [Fact]
    public void A_torn_final_line_without_a_newline_does_not_swallow_the_next_record()
    {
        var store = CreateStore();
        store.Add(Record("intact"));
        File.AppendAllText(Path_, "{\"FinalText\":\"torn");

        var reopened = CreateStore();
        reopened.Add(Record("after the tear"));

        Assert.Contains("after the tear", CreateStore().Recent.Select(r => r.FinalText));
        Assert.Contains("intact", CreateStore().Recent.Select(r => r.FinalText));
    }

    [Fact]
    public void Clearing_reports_success_and_leaves_nothing_behind()
    {
        var store = CreateStore();
        store.Add(Record("something"));

        Assert.True(store.Clear());
        Assert.False(File.Exists(Path_));
        Assert.Empty(CreateStore().Recent);
    }

    /// <summary>
    /// An empty list is not proof of deletion. When the file survives, the caller has to be able to
    /// say so rather than showing an empty history that returns on the next launch.
    /// </summary>
    [Fact]
    public void Clearing_reports_failure_when_the_file_cannot_be_deleted()
    {
        var store = CreateStore();
        store.Add(Record("something"));

        using (File.Open(Path_, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.Clear());
        }

        Assert.True(File.Exists(Path_));
        Assert.Single(CreateStore().Recent);
    }

    [Fact]
    public void Compaction_leaves_no_temporary_file_behind()
    {
        _settings.Current.History.MaxEntries = 5;
        var store = CreateStore();
        for (var i = 0; i < 100; i++)
        {
            store.Add(Record($"entry {i}"));
        }

        Assert.Empty(Directory.GetFiles(_root, "*.compacting"));
    }
}
