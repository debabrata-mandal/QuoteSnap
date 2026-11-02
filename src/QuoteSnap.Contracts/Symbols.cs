namespace QuoteSnap.Contracts;

/// <param name="SymbolCount">Listed ISINs with a Fyers symbol.</param>
/// <param name="LoadedAt">When Fyers' symbol masters were last downloaded; null until the first refresh.</param>
public sealed record FyersSymbolStatus(int SymbolCount, DateTimeOffset? LoadedAt);

/// <param name="SymbolCount">Listed ISINs with a Fyers symbol: <paramref name="NseCount"/> plus <paramref name="BseOnlyCount"/>.</param>
/// <param name="NseCount">ISINs given their NSE symbol.</param>
/// <param name="BseOnlyCount">ISINs listed only on BSE, given their BSE symbol.</param>
public sealed record FyersSymbolRefreshResult(int SymbolCount, int NseCount, int BseOnlyCount, DateTimeOffset LoadedAt);

/// <summary>A listed stock found by name, ticker or ISIN.</summary>
/// <param name="Symbol">The Fyers symbol to request quotes with, such as NSE:TATAMOTORS-EQ; the NSE one when the stock is listed there.</param>
/// <param name="Name">The company name as Fyers lists it, such as TATA MOTORS LIMITED.</param>
public sealed record FyersSymbolMatch(string Symbol, string Name, string Isin);

/// <param name="Symbols">Best matches first: exact tickers, then tickers and names starting with the search.</param>
/// <param name="LoadedAt">When the searched symbols were downloaded.</param>
public sealed record FyersSymbolSearchResult(string Query, IReadOnlyList<FyersSymbolMatch> Symbols, DateTimeOffset LoadedAt);
