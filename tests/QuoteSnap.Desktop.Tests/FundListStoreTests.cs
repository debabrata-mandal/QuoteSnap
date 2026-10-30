using System.IO;
using QuoteSnap.Desktop.Settings;

namespace QuoteSnap.Desktop.Tests;

public sealed class FundListStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quotesnap-desktop-tests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_missing_file_is_an_empty_list()
    {
        (IReadOnlyList<SavedFund> funds, string? problem) = new FundListStore(Path.Combine(_root, "my-funds.json")).Load();

        Assert.Empty(funds);
        Assert.Null(problem);
    }

    [Fact]
    public void The_list_round_trips_in_order()
    {
        FundListStore store = new(Path.Combine(_root, "nested", "my-funds.json"));
        SavedFund[] saved =
        [
            new(118763, "Nippon India Power & Infra Fund", "Nippon India Mutual Fund", "Growth Option", "Open Ended Schemes(Equity Scheme - Sectoral/ Thematic)"),
            new(119076, "DSP Flexi Cap Fund", "DSP Mutual Fund", "Growth", null),
        ];

        store.Save(saved);
        (IReadOnlyList<SavedFund> loaded, string? problem) = store.Load();

        Assert.Null(problem);
        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void An_unreadable_file_is_kept_as_a_backup_instead_of_being_overwritten()
    {
        string path = Path.Combine(_root, "my-funds.json");
        File.WriteAllText(path, "{ not json");
        FundListStore store = new(path);

        (IReadOnlyList<SavedFund> funds, string? problem) = store.Load();

        Assert.Empty(funds);
        Assert.Contains(".bak", problem);
        Assert.Equal("{ not json", File.ReadAllText(path + ".bak"));
        Assert.False(File.Exists(path));
    }
}
