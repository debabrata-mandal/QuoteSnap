using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Nav;

public sealed class NavOptions
{
    public const string SectionName = "Nav";

    [Required, Url]
    public string SourceUrl { get; set; } = "https://portal.amfiindia.com/spages/NAVAll.txt";

    /// <summary>
    /// AMFI's scheme master CSV, used to tell which schemes are Direct Plans when the NAV file leaves Plan blank.
    /// Empty skips the download, and those schemes are left out.
    /// </summary>
    public string SchemeMasterUrl { get; set; } = "https://portal.amfiindia.com/DownloadSchemeData_Po.aspx?mf=0";
}
