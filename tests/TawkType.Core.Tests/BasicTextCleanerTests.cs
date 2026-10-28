using TawkType.Core.Text;

namespace TawkType.Core.Tests;

public sealed class BasicTextCleanerTests
{
    [Theory]
    [InlineData("um so, uh, I think we should ship it", "So, I think we should ship it")]
    [InlineData("hello world", "Hello world")]
    [InlineData("   spaced   out   text  ", "Spaced out text")]
    [InlineData("Erm, the summit is on Thursday.", "The summit is on Thursday.")]
    [InlineData("I hummed a tune", "I hummed a tune")]
    [InlineData("so...", "So.")]
    [InlineData("I opened it as administrator, so...", "I opened it as administrator, so.")]
    [InlineData("well . . . maybe", "Well. maybe")]
    [InlineData("yes, um, .", "Yes.")]
    [InlineData("really?!", "Really!")]
    [InlineData("hello , world !", "Hello, world!")]
    [InlineData("the literal text is $1", "The literal text is $1")]
    [InlineData("", "")]
    public void Removes_fillers_and_normalises(string input, string expected)
    {
        Assert.Equal(expected, BasicTextCleaner.Clean(input, removeFillers: true));
    }

    [Fact]
    public void Keeps_fillers_when_disabled()
    {
        Assert.Equal("Um hello", BasicTextCleaner.Clean("um hello", removeFillers: false));
    }
}
