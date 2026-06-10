using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AurSync.Updater;

internal static class GitHubReleaseClient
{
    internal static HttpClient CreateGithubClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("aur-sync-hub", "1.0"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    internal static async Task<string> GetLatestReleaseTagAsync(
        HttpClient http,
        string repo,
        bool allowPrerelease,
        CancellationToken cancellationToken)
    {
        var url = allowPrerelease
            ? $"https://api.github.com/repos/{repo}/releases?per_page=10"
            : $"https://api.github.com/repos/{repo}/releases/latest";

        using var response = await http.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(allowPrerelease
                ? $"Repository not found: {repo}"
                : $"No latest release found for {repo} (prerelease-only repos need allow_prerelease: true)");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub API error for {repo}: HTTP {(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim());
        }

        return allowPrerelease
            ? SelectTagFromReleaseList(body, repo)
            : SelectTagFromLatestRelease(body, repo);
    }

    internal static string SelectTagFromLatestRelease(string json, string repo)
    {
        using var doc = JsonDocument.Parse(json);
        return ReadTagName(doc.RootElement)
            ?? throw new InvalidOperationException($"No valid tag_name in latest release for {repo}");
    }

    internal static string SelectTagFromReleaseList(string json, string repo)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (ReadTagName(release) is { } tag)
            {
                return tag;
            }
        }

        throw new InvalidOperationException($"No published release found for {repo}");
    }

    private static string? ReadTagName(JsonElement release)
    {
        if (!release.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var tag = tagElement.GetString();
        return string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
    }

    /// <summary>
    /// Maps an upstream version to a pacman-legal pkgver: hyphens are invalid
    /// in pkgver, so "1.0.0-rc1" becomes "1.0.0_rc1" (which vercmp correctly
    /// orders before "1.0.0"). PKGBUILDs reconstruct the tag with
    /// `_pkgtag="v${pkgver//_/-}"`.
    /// </summary>
    internal static string NormalizeVersion(string version)
    {
        return version.Replace('-', '_');
    }

    internal static string StripPrefix(string tag, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return tag;
        }

        if (tag.StartsWith(prefix, StringComparison.Ordinal))
        {
            return tag[prefix.Length..];
        }

        throw new InvalidOperationException(
            $"Release tag '{tag}' does not start with configured prefix '{prefix}'");
    }
}
