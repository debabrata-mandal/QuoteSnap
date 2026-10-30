using QuoteSnap.Api.Domain;

namespace QuoteSnap.Tests;

public sealed class SymbolValidatorTests
{
    [Fact]
    public void Normalizes_case_and_whitespace_and_removes_duplicates_in_order()
    {
        SymbolValidationResult result = SymbolValidator.Validate([" nse:sbin-eq ", "NSE:RELIANCE-EQ", "NSE:SBIN-EQ"]);

        Assert.Equal(["NSE:SBIN-EQ", "NSE:RELIANCE-EQ"], result.Valid);
        Assert.Empty(result.Invalid);
    }

    [Theory]
    [InlineData("NSE:SBIN-EQ")]
    [InlineData("BSE:SENSEX-INDEX")]
    [InlineData("NSE:NIFTY50-INDEX")]
    [InlineData("NSE:M&M-EQ")]
    [InlineData("NSE:BAJAJ-AUTO-EQ")]
    [InlineData("NSE:NIFTY25NOVFUT")]
    [InlineData("MCX:CRUDEOIL25NOVFUT")]
    public void Accepts_valid_symbols(string symbol) =>
        Assert.Equal([symbol], SymbolValidator.Validate([symbol]).Valid);

    [Theory]
    [InlineData("SBIN")]
    [InlineData("NYSE:IBM")]
    [InlineData("NSE:")]
    [InlineData("NSE:SBIN EQ")]
    [InlineData("NSE SBIN-EQ")]
    public void Rejects_invalid_symbols(string symbol)
    {
        SymbolValidationResult result = SymbolValidator.Validate([symbol]);

        Assert.Empty(result.Valid);
        Assert.Single(result.Invalid);
    }

    [Fact]
    public void Drops_blank_entries()
    {
        SymbolValidationResult result = SymbolValidator.Validate(["", "   ", null, "NSE:SBIN-EQ"]);

        Assert.Single(result.Entries);
    }

    [Fact]
    public void Truncates_overlong_entries_and_marks_them_invalid()
    {
        SymbolValidationResult result = SymbolValidator.Validate(["NSE:" + new string('A', 100)]);

        SymbolEntry entry = Assert.Single(result.Entries);
        Assert.False(entry.IsValid);
        Assert.Equal(SymbolValidator.MaxSymbolLength, entry.Symbol.Length);
    }

    [Fact]
    public void Keeps_the_callers_order_across_valid_and_invalid_entries()
    {
        SymbolValidationResult result = SymbolValidator.Validate(["NSE:SBIN-EQ", "bad", "NSE:TCS-EQ"]);

        Assert.Equal(["NSE:SBIN-EQ", "BAD", "NSE:TCS-EQ"], result.Entries.Select(e => e.Symbol));
        Assert.Equal([true, false, true], result.Entries.Select(e => e.IsValid));
    }
}
