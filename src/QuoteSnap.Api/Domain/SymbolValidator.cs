using System.Text.RegularExpressions;

namespace QuoteSnap.Api.Domain;

public sealed record SymbolEntry(string Symbol, bool IsValid);

public sealed class SymbolValidationResult(IReadOnlyList<SymbolEntry> entries)
{
    /// <summary>Distinct, normalized symbols in the caller's order.</summary>
    public IReadOnlyList<SymbolEntry> Entries { get; } = entries;

    public IReadOnlyList<string> Valid { get; } = entries.Where(e => e.IsValid).Select(e => e.Symbol).ToList();

    public IReadOnlyList<string> Invalid { get; } = entries.Where(e => !e.IsValid).Select(e => e.Symbol).ToList();
}

public static partial class SymbolValidator
{
    public const int MaxSymbolLength = 64;

    public const string InvalidSymbolMessage =
        "Invalid symbol format. Expected EXCHANGE:SYMBOL-SERIES, for example NSE:SBIN-EQ.";

    [GeneratedRegex(@"^(NSE|BSE|MCX):[A-Z0-9&_.\-]+$")]
    private static partial Regex SymbolPattern();

    /// <summary>Trims, uppercases and de-duplicates symbols; blank entries are dropped.</summary>
    public static SymbolValidationResult Validate(IEnumerable<string?> symbols)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<SymbolEntry> entries = new();

        foreach (string? raw in symbols)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string symbol = raw.Trim().ToUpperInvariant();
            bool isValid = symbol.Length <= MaxSymbolLength && SymbolPattern().IsMatch(symbol);
            if (symbol.Length > MaxSymbolLength)
                symbol = symbol[..MaxSymbolLength];

            if (seen.Add(symbol))
                entries.Add(new SymbolEntry(symbol, isValid));
        }

        return new SymbolValidationResult(entries);
    }
}
