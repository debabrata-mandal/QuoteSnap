using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Holdings;

namespace QuoteSnap.Tests;

public sealed class FyersSymbolMasterTests
{
    private static Dictionary<string, (string Symbol, string Name)> Summarize(IEnumerable<FyersSymbol> symbols) =>
        symbols.ToDictionary(s => s.Isin, s => (s.Symbol, s.Name));

    [Fact]
    public void Reads_isin_symbol_and_name_from_the_current_and_older_layouts()
    {
        Dictionary<string, FyersSymbol> symbols = FyersSymbolMaster.Parse("""
            10100000002885,RELIANCE INDUSTRIES LTD,0,1,0.1,INE002A01018,0915-1530|1815-1915:,2026-10-30,,NSE:RELIANCE-EQ,10,10,2885,RELIANCE,2885,-1.0,XX,10100000002885,None,1,3.7
            10100000003456,TATA MOTORS LIMITED,0,1,0.05,INE155A01022,0915-1530|1815-1915:,2023-11-03,NSE:TATAMOTORS-EQ,10,10,3456,TATAMOTORS,3456,-1.0,XX,10100000003456,None

            """.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(
            new Dictionary<string, (string, string)>
            {
                ["INE002A01018"] = ("NSE:RELIANCE-EQ", "RELIANCE INDUSTRIES LTD"),
                ["INE155A01022"] = ("NSE:TATAMOTORS-EQ", "TATA MOTORS LIMITED"),
            },
            Summarize(symbols.Values));
    }

    [Fact]
    public void Skips_lines_without_an_isin_and_keeps_the_first_symbol_of_an_isin()
    {
        Dictionary<string, FyersSymbol> symbols = FyersSymbolMaster.Parse("""
            101000000026000,NIFTY 50,0,1,0.05,,0915-1530|1815-1915:,2026-10-30,,NSE:NIFTY50-INDEX,10,10,26000,NIFTY50,26000,-1.0,XX,101000000026000,None,0,0.0
            10100000001333,HDFC BANK LTD,0,1,0.1,INE040A01034,0915-1530|1815-1915:,2026-10-30,,NSE:HDFCBANK-EQ,10,10,1333,HDFCBANK,1333,-1.0,XX,10100000001333,None,1,3.7
            10100000001334,HDFC BANK LTD,0,1,0.1,INE040A01034,0915-1530|1815-1915:,2026-10-30,,NSE:HDFCBANK-BE,10,10,1334,HDFCBANK,1334,-1.0,XX,10100000001334,None,1,3.7
            """);

        Assert.Equal(
            new Dictionary<string, (string, string)> { ["INE040A01034"] = ("NSE:HDFCBANK-EQ", "HDFC BANK LTD") },
            Summarize(symbols.Values));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Not found</body></html>")]
    public void Rejects_a_file_that_is_not_a_symbol_master(string content) =>
        Assert.Throws<FormatException>(() => FyersSymbolMaster.Parse(content));

    [Fact]
    public void Prefers_nse_and_falls_back_to_bse()
    {
        Dictionary<string, FyersSymbol> nse = FyersSymbolMaster.Parse(
            "10100000002885,RELIANCE INDUSTRIES LTD,0,1,0.1,INE002A01018,0915-1530|1815-1915:,2026-10-30,,NSE:RELIANCE-EQ,10,10,2885,RELIANCE,2885,-1.0,XX,10100000002885,None,1,3.7");
        Dictionary<string, FyersSymbol> bse = FyersSymbolMaster.Parse("""
            1210000000500325,RELIANCE INDUSTRIES LTD.,0,1,0.05,INE002A01018,0915-1530|1815-1915:,2026-10-30,,BSE:RELIANCE-A,12,10,500325,RELIANCE,500325,-1.0,XX,1210000000500325,None,0,0.0
            1210000000523395,3M INDIA LTD.,0,1,0.05,INE470A01017,0915-1530|1815-1915:,2026-10-30,,BSE:3MINDIA-A,12,10,523395,3MINDIA,523395,-1.0,XX,1210000000523395,None,0,0.0
            """);

        List<FyersSymbol> combined = FyersSymbolMaster.Combine(nse, bse);

        Assert.Equal(
            new Dictionary<string, (string, string)>
            {
                ["INE002A01018"] = ("NSE:RELIANCE-EQ", "RELIANCE INDUSTRIES LTD"),
                ["INE470A01017"] = ("BSE:3MINDIA-A", "3M INDIA LTD."),
            },
            Summarize(combined));
    }
}
