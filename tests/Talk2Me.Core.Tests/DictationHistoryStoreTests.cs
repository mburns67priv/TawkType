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
}
