using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Nav;

public sealed class NavOptions
{
    public const string SectionName = "Nav";

    [Required, Url]
    public string SourceUrl { get; set; } = "https://portal.amfiindia.com/spages/NAVAll.txt";
}
