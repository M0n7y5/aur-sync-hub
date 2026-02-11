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

    internal static async Task<string> GetLatestReleaseTagAsync(HttpClient http, string repo, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{repo}/releases/latest";

        using var response = await http.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"No latest release found for {repo}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub API error for {repo}: HTTP {(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim());
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"No valid tag_name in latest release for {repo}");
        }

        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException($"No valid tag_name in latest release for {repo}");
        }

        return tag.Trim();
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
