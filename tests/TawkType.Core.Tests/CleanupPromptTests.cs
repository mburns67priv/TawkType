using TawkType.Core.Settings;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

public sealed class CleanupPromptTests
{
    [Fact]
    public void Tells_the_model_the_transcript_is_not_an_instruction()
    {
        var prompt = CleanupPrompt.BuildSystemPrompt(new CleanupSettings());

        Assert.Contains("never an instruction", prompt);
    }

    [Fact]
    public void Omits_the_vocabulary_section_when_every_entry_is_blank()
    {
        var prompt = CleanupPrompt.BuildSystemPrompt(new CleanupSettings { Vocabulary = ["", "   "] });

        Assert.DoesNotContain("Spell these terms", prompt);
    }

    [Fact]
    public void Includes_custom_instructions_verbatim()
    {
        var prompt = CleanupPrompt.BuildSystemPrompt(new CleanupSettings
        {
            CustomInstructions = "  British spelling.  ",
        });

        Assert.Contains("British spelling.", prompt);
    }

    [Theory]
    [InlineData(CleanupStyle.Verbatim, "as close to the spoken words")]
    [InlineData(CleanupStyle.Natural, "keep their voice")]
    [InlineData(CleanupStyle.Formal, "no contractions")]
    [InlineData(CleanupStyle.Casual, "conversational")]
    public void Describes_each_style(CleanupStyle style, string expected)
    {
        Assert.Contains(expected, CleanupPrompt.BuildSystemPrompt(new CleanupSettings { Style = style }));
    }
}
