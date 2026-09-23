using System.Text.RegularExpressions;

namespace PhotoAlbum.Tests.PostMigration.Infrastructure;

/// <summary>
/// Harvests the antiforgery cookie + hidden form field pair required by every `POST` handler
/// in the frozen spec's General Preconditions (`/?handler=Upload`, `/Detail/{id}?handler=Delete`,
/// `/Login`). ASP.NET Core's default antiforgery tokens are not page-scoped, so a single `GET`
/// to any page rendering `@Html.AntiForgeryToken()` (the gallery page's upload form) is
/// sufficient to obtain a token pair valid for POSTs to any page, as long as the same
/// cookie-enabled <see cref="HttpClient"/> instance is reused for both the priming `GET` and
/// the subsequent `POST`.
/// </summary>
public static class AntiforgeryHelper
{
    private static readonly Regex TokenInputRegex = new(
        "<input[^>]*name=\"__RequestVerificationToken\"[^>]*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ValueAttributeRegex = new(
        "value=\"([^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Issues <c>GET /</c> (or <paramref name="primingUrl"/>) via <paramref name="client"/> and
    /// extracts the <c>__RequestVerificationToken</c> hidden field value. The matching
    /// antiforgery cookie is captured automatically by the client's cookie container.
    /// </summary>
    public static async Task<string> GetTokenAsync(HttpClient client, string primingUrl = "/")
    {
        var response = await client.GetAsync(primingUrl);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var inputMatch = TokenInputRegex.Match(html);
        if (!inputMatch.Success)
        {
            throw new InvalidOperationException(
                $"Could not locate an antiforgery hidden field in the response body of GET {primingUrl}.");
        }

        var valueMatch = ValueAttributeRegex.Match(inputMatch.Value);
        if (!valueMatch.Success)
        {
            throw new InvalidOperationException(
                $"Located an antiforgery input tag but could not extract its value attribute: {inputMatch.Value}");
        }

        return valueMatch.Groups[1].Value;
    }
}
