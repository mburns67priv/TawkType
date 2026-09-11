using TawkType.Core.Settings;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

public class DeliveryTests
{
    /// <summary>
    /// The reason this rule exists: typing a line break means synthesising Return, which sends the
    /// message in a chat composer and runs the line in a terminal. The LLM cleanup pass produces
    /// paragraphs and lists, so multiline results are ordinary, not exotic.
    /// </summary>
    [Theory]
    [InlineData(TextInjectionMode.Auto)]
    [InlineData(TextInjectionMode.TypeUnicode)]
    [InlineData(TextInjectionMode.Paste)]
    public void Multiline_text_always_pastes_whatever_the_user_chose(TextInjectionMode mode)
    {
        Assert.True(Delivery.ShouldPaste("first paragraph\n\nsecond paragraph", mode));
    }

    [Theory]
    [InlineData("one\ntwo")]
    [InlineData("one\r\ntwo")]
    [InlineData("one\rtwo")]
    [InlineData("trailing\n")]
    public void Every_kind_of_line_break_counts_as_multiline(string text)
    {
        Assert.True(Delivery.IsMultiline(text));
        Assert.True(Delivery.ShouldPaste(text, TextInjectionMode.TypeUnicode));
    }

    [Fact]
    public void Short_single_line_text_is_still_typed()
    {
        Assert.False(Delivery.ShouldPaste("just a sentence", TextInjectionMode.Auto));
        Assert.False(Delivery.ShouldPaste("just a sentence", TextInjectionMode.TypeUnicode));
    }

    [Fact]
    public void Long_single_line_text_pastes_on_Auto_only()
    {
        var long_ = new string('a', Delivery.PasteThresholdChars + 1);

        Assert.True(Delivery.ShouldPaste(long_, TextInjectionMode.Auto));
        Assert.False(Delivery.ShouldPaste(long_, TextInjectionMode.TypeUnicode));
    }

    [Theory]
    [InlineData("one\ntwo", "one two")]
    [InlineData("one\r\ntwo", "one two")]
    [InlineData("one\n\ntwo", "one two")]
    [InlineData("one \n  two", "one two")]
    [InlineData("\nleading and trailing\n", "leading and trailing")]
    public void Folding_a_line_break_leaves_one_space(string text, string expected)
    {
        Assert.Equal(expected, Delivery.SingleLine(text));
    }

    [Fact]
    public void Folding_leaves_single_line_text_alone()
    {
        Assert.Equal("nothing to fold", Delivery.SingleLine("nothing to fold"));
    }
}
