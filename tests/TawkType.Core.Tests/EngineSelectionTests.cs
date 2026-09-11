using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

public sealed class EngineSelectionTests
{
    [Theory]
    [InlineData(TranscriptionEngine.Auto, "en", TranscriptionEngine.Parakeet)]
    [InlineData(TranscriptionEngine.Auto, "EN", TranscriptionEngine.Parakeet)]
    [InlineData(TranscriptionEngine.Auto, "de", TranscriptionEngine.Parakeet)]
    [InlineData(TranscriptionEngine.Auto, "ja", TranscriptionEngine.Whisper)]
    [InlineData(TranscriptionEngine.Auto, "auto", TranscriptionEngine.Whisper)]
    [InlineData(TranscriptionEngine.Auto, "", TranscriptionEngine.Whisper)]
    [InlineData(TranscriptionEngine.Parakeet, "ja", TranscriptionEngine.Parakeet)]
    [InlineData(TranscriptionEngine.Whisper, "en", TranscriptionEngine.Whisper)]
    public void Resolves_engine_from_setting_and_language(TranscriptionEngine requested, string language, TranscriptionEngine expected)
    {
        Assert.Equal(expected, EngineSelection.Resolve(requested, language));
    }

    [Fact]
    public void Parakeet_covers_25_languages()
    {
        Assert.Equal(25, EngineSelection.ParakeetLanguages.Count);
    }
}
