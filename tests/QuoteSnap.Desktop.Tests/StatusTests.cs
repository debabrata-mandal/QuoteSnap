using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Controls;
using QuoteSnap.Desktop.Text;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Tests;

public sealed class StatusTests
{
    [Theory]
    [InlineData(QuoteRequestStatus.CompletedWithErrors, "Completed with errors")]
    [InlineData(QuoteRequestStatus.Running, "Running")]
    [InlineData(QuoteResultStatus.Ok, "OK")]
    [InlineData(QuoteResultStatus.Invalid, "Invalid")]
    public void Statuses_read_as_words(object status, string expected)
    {
        Assert.Equal(expected, StatusText.Of(status));
    }

    [Theory]
    [InlineData(QuoteRequestStatus.Queued, Tone.Neutral)]
    [InlineData(QuoteRequestStatus.Running, Tone.Info)]
    [InlineData(QuoteRequestStatus.Completed, Tone.Success)]
    [InlineData(QuoteRequestStatus.CompletedWithErrors, Tone.Warning)]
    [InlineData(QuoteRequestStatus.Failed, Tone.Danger)]
    [InlineData(QuoteResultStatus.Ok, Tone.Success)]
    [InlineData(QuoteResultStatus.Error, Tone.Danger)]
    [InlineData(QuoteResultStatus.Invalid, Tone.Warning)]
    [InlineData(QuoteResultStatus.Pending, Tone.Neutral)]
    [InlineData(StatusLevel.Busy, Tone.Info)]
    [InlineData(StatusLevel.Unknown, Tone.Neutral)]
    public void Statuses_map_to_tones(object status, Tone expected)
    {
        Assert.Equal(expected, Tones.Of(status));
    }

    [Fact]
    public void One_invalid_symbol_is_singular()
    {
        Assert.Equal("1 symbol isn't a valid Fyers symbol and wasn't fetched: FOO.", NewRequestViewModel.DescribeInvalid(["FOO"]));
    }

    [Fact]
    public void Many_invalid_symbols_are_listed_up_to_a_limit()
    {
        string[] invalid = [.. Enumerable.Range(1, 14).Select(i => $"BAD{i}")];

        string? text = NewRequestViewModel.DescribeInvalid(invalid);

        Assert.StartsWith("14 symbols aren't valid Fyers symbols and weren't fetched: BAD1, BAD2,", text, StringComparison.Ordinal);
        Assert.EndsWith("BAD12 and 2 more.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void No_invalid_symbols_means_no_message()
    {
        Assert.Null(NewRequestViewModel.DescribeInvalid([]));
    }
}
