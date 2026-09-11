using System.Globalization;
using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

public class NumberFieldTests
{
    private static readonly NumberField Whole = new(1, 100, "entries");

    private static readonly NumberField Fractional = new(0, 10, "minutes", Whole: false);

    [Fact]
    public void A_number_in_range_comes_back()
    {
        var result = Whole.Parse(" 42 ");

        Assert.True(result.Ok);
        Assert.Equal(42, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_box_is_not_a_number(string? text) => Assert.False(Whole.Parse(text).Ok);

    [Theory]
    [InlineData("ten")]
    [InlineData("4 entries")]
    [InlineData("1,2,3")]
    public void Something_that_is_not_a_number_is_refused(string text) => Assert.False(Whole.Parse(text).Ok);

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-5")]
    public void A_number_outside_the_range_is_refused(string text) => Assert.False(Whole.Parse(text).Ok);

    [Theory]
    [InlineData("1")]
    [InlineData("100")]
    public void Both_ends_of_the_range_are_allowed(string text) => Assert.True(Whole.Parse(text).Ok);

    [Fact]
    public void A_fraction_is_refused_where_only_whole_numbers_make_sense()
        => Assert.False(Whole.Parse("2.5").Ok);

    [Fact]
    public void A_fraction_is_allowed_where_it_makes_sense()
    {
        var result = Fractional.Parse("2.5");

        Assert.True(result.Ok);
        Assert.Equal(2.5, result.Value);
    }

    /// <summary>
    /// Someone on a comma-decimal machine who types a point out of habit means half a minute, not
    /// twenty-five of them, and certainly not an error.
    /// </summary>
    [Fact]
    public void A_point_is_understood_on_a_comma_decimal_machine()
    {
        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            Assert.Equal(2.5, Fractional.Parse("2.5").Value);
            Assert.Equal(2.5, Fractional.Parse("2,5").Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    /// <summary>The message has to stand on its own: it sits under the box, away from the label.</summary>
    [Fact]
    public void The_message_names_the_unit_and_the_range()
    {
        var error = Whole.Parse("500").Error;

        Assert.NotNull(error);
        Assert.Contains("entries", error);
        Assert.Contains("100", error);
    }

    [Fact]
    public void A_whole_field_is_written_back_without_a_decimal_point()
        => Assert.Equal("42", Whole.Format(42));
}
