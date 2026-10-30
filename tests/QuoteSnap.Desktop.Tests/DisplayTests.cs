using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.Tests;

public sealed class DisplayTests
{
    [Theory]
    [InlineData(4.94, "4.9 s")]
    [InlineData(125, "2 min 5 s")]
    [InlineData(3 * 3600 + 20 * 60, "3 h 20 min")]
    [InlineData(30 * 3600, "1 day 6 h")]
    [InlineData(354 * 3600 + 22 * 60, "14 days 18 h")]
    public void Durations_use_the_largest_sensible_unit(double seconds, string expected)
    {
        Assert.Equal(expected, Display.Duration(TimeSpan.FromSeconds(seconds)).Replace(',', '.'));
    }

    [Fact]
    public void A_missing_duration_is_a_dash()
    {
        Assert.Equal(Display.Missing, Display.Duration(null));
    }

    [Theory]
    [InlineData("1.0.0+7dc8006187cab0d189fc0137388f748a18844ac7", "1.0.0 (7dc8006)")]
    [InlineData("1.0.0+abc", "1.0.0 (abc)")]
    [InlineData("1.0.0", "1.0.0")]
    public void Versions_shorten_the_commit_hash(string version, string expected)
    {
        Assert.Equal(expected, Display.Version(version));
    }

    [Fact]
    public void Times_name_today_and_yesterday()
    {
        DateTime today = new(2026, 10, 30);
        DateTimeOffset At(int day, int hour) => new(new DateTime(2026, 10, day, hour, 5, 9, DateTimeKind.Local));

        Assert.Equal("Today, 11:05:09", Display.When(At(30, 11), today));
        Assert.Equal("Yesterday, 18:05:09", Display.When(At(29, 18), today));
        Assert.Equal("28 Oct, 09:05:09", Display.When(At(28, 9), today), ignoreCase: true);
        Assert.Equal(Display.Missing, Display.When(null, today));
    }

    [Fact]
    public void Times_in_a_sentence_start_lower_case_or_with_on()
    {
        DateTime today = new(2026, 10, 30);
        DateTimeOffset At(int day) => new(new DateTime(2026, 10, day, 6, 0, 0, DateTimeKind.Local));

        Assert.Equal("today, 06:00:00", Display.WhenInSentence(At(30), today));
        Assert.Equal("yesterday, 06:00:00", Display.WhenInSentence(At(29), today));
        Assert.StartsWith("on 27 ", Display.WhenInSentence(At(27), today), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1, "1 symbol")]
    [InlineData(2, "2 symbols")]
    [InlineData(0, "0 symbols")]
    public void Counts_are_pluralised(int count, string expected)
    {
        Assert.Equal(expected, Display.Count(count, "symbol"));
    }

    [Fact]
    public void Counts_take_an_irregular_plural()
    {
        Assert.Equal("3 categories", Display.Count(3, "category", "categories"));
    }

    [Fact]
    public void Short_ids_are_the_end_of_the_guid()
    {
        Assert.Equal("4a1b2c3d", Display.ShortId(Guid.Parse("01a18b8c-0000-7000-8000-00004a1b2c3d")));
    }

    [Theory]
    [InlineData("Direct Plan", "Direct")]
    [InlineData("Growth Option", "Growth")]
    [InlineData("GROWTH", "Growth")]
    [InlineData("IDCW Payout", "IDCW Payout")]
    public void Plan_and_option_labels_are_shortened(string label, string expected)
    {
        Assert.Equal(expected, Display.ShortLabel(label));
    }
}
