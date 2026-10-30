using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Options;

public sealed class QuoteSnapOptions
{
    public const string SectionName = "QuoteSnap";

    /// <summary>Holds the Data Protection keys. Relative paths resolve against the content root.</summary>
    [Required]
    public string DataDirectory { get; set; } = "local-data";

    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;

    public string? DataProtectionCertificatePath { get; set; }

    public string? DataProtectionCertificatePassword { get; set; }

    public string KeysDirectory => Path.Combine(DataDirectory, "keys");
}
