using Talk2Me.Core.Settings;

namespace Talk2Me.Core.Tests;

public class LanguagesTests
{
    [Fact]
    public void All_is_sorted_by_name()
    {
        var names = Languages.All.Select(language => language.Name).ToArray();

        Assert.Equal(names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase), names);
    }

    [Fact]
    public void MostSpoken_keeps_the_order_it_is_declared_in()
    {
        Assert.Equal(Languages.MostSpokenCodes, Languages.MostSpoken.Select(language => language.Code));
    }

    [Fact]
    public void Rest_excludes_the_ones_offered_at_the_top()
    {
        Assert.DoesNotContain(Languages.Rest, language => Languages.MostSpokenCodes.Contains(language.Code));
        Assert.Equal(Languages.All.Count, Languages.Rest.Count + Languages.MostSpoken.Count);
    }

    [Fact]
    public void Every_language_Parakeet_covers_is_offered()
    {
        foreach (var code in EngineSelection.ParakeetLanguages)
        {
            Assert.NotNull(Languages.Find(code));
        }
    }

    [Theory]
    [InlineData("EN", "en")]
    [InlineData(" fr ", "fr")]
    [InlineData("auto", Languages.AutoCode)]
    public void Find_is_forgiving_about_case_and_spacing(string input, string expected)
    {
        Assert.Equal(expected, Languages.Find(input)?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("klingon")]
    public void Find_returns_null_for_anything_it_does_not_know(string input)
    {
        Assert.Null(Languages.Find(input));
    }
}
