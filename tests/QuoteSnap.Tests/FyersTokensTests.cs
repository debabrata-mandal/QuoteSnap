using System.Buffers.Text;
using System.Text;
using QuoteSnap.Api.Fyers;

namespace QuoteSnap.Tests;

public sealed class FyersTokensTests
{
    [Fact]
    public void AppIdHash_is_lowercase_sha256_of_app_id_and_secret()
    {
        Assert.Equal(
            "26203879e10aa1105d136052748a6dd54dd0b99e197d276edf8f3728c91273ab",
            FyersTokens.AppIdHash("XJ12345-100", "MYSECRET"));
    }

    [Fact]
    public void ReadJwtExpiry_reads_the_exp_claim()
    {
        string token = Jwt("""{"sub":"XT0001","exp":1761800000}""");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1761800000), FyersTokens.ReadJwtExpiry(token));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public void ReadJwtExpiry_returns_null_for_unreadable_tokens(string token) =>
        Assert.Null(FyersTokens.ReadJwtExpiry(token));

    [Fact]
    public void ReadJwtExpiry_returns_null_when_there_is_no_exp_claim() =>
        Assert.Null(FyersTokens.ReadJwtExpiry(Jwt("""{"sub":"XT0001"}""")));

    private static string Jwt(string payloadJson) =>
        $"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"none"}"""))}"
        + $".{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson))}.signature";
}
