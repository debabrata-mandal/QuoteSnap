using System.IO;
using System.Text.Json;

namespace QuoteSnap.Desktop.Settings;

/// <summary>A fund on the user's list. The descriptive fields let the list show before the API answers.</summary>
public sealed record SavedFund(int SchemeCode, string SchemeName, string? FundHouse, string? Option, string? Category);

/// <summary>The user's mutual fund list, saved as JSON in %LOCALAPPDATA%\QuoteSnap.</summary>
public sealed class FundListStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static FundListStore CreateDefault() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuoteSnap", "my-funds.json"));

    public string FilePath { get; } = path;

    /// <summary>
    /// An unreadable file is renamed to .bak rather than overwritten by the next save, so the list can be recovered.
    /// </summary>
    /// <returns>The funds, and a problem to show if the file couldn't be read.</returns>
    public (IReadOnlyList<SavedFund> Funds, string? Problem) Load()
    {
        if (!File.Exists(FilePath))
            return ([], null);

        try
        {
            StoredList? stored = JsonSerializer.Deserialize<StoredList>(File.ReadAllText(FilePath), Json);
            return ([.. (stored?.Funds ?? []).Where(f => f.SchemeCode > 0 && !string.IsNullOrWhiteSpace(f.SchemeName)).DistinctBy(f => f.SchemeCode)], null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            string backup = FilePath + ".bak";
            File.Copy(FilePath, backup, overwrite: true);
            File.Delete(FilePath);
            return ([], $"Your fund list couldn't be read ({ex.Message}). It was moved to {backup}.");
        }
    }

    public void Save(IEnumerable<SavedFund> funds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new StoredList(1, [.. funds]), Json));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private sealed record StoredList(int Version, IReadOnlyList<SavedFund> Funds);
}
