using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    private const string HowToSet = " Set Auth__{0} in the repository's .env locally (see .env.example),"
        + " or as an environment variable in Docker.";

    /// <summary>Required in the X-Api-Key header for /api endpoints.</summary>
    [Required(ErrorMessage = "Auth:ApiKey is not set." + HowToSet)]
    [MinLength(16, ErrorMessage = "Auth:ApiKey must be at least 16 characters.")]
    public string ApiKey { get; set; } = "";

    /// <summary>Required in the X-Admin-Key header for /admin endpoints.</summary>
    [Required(ErrorMessage = "Auth:AdminApiKey is not set." + HowToSet)]
    [MinLength(16, ErrorMessage = "Auth:AdminApiKey must be at least 16 characters.")]
    public string AdminApiKey { get; set; } = "";
}
