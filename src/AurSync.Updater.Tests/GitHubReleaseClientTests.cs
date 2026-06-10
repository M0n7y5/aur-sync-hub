namespace AurSync.Updater.Tests;

public class GitHubReleaseClientTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.0.0-rc1", "1.0.0_rc1")]
    [InlineData("2.0.0-beta-1", "2.0.0_beta_1")]
    public void NormalizeVersion_MapsHyphensToUnderscores(string input, string expected)
    {
        Assert.Equal(expected, GitHubReleaseClient.NormalizeVersion(input));
    }

    [Fact]
    public void SelectTagFromReleaseList_SkipsDraftsAndAcceptsPrereleases()
    {
        const string json = """
            [
              { "tag_name": "v2.0.0-draft", "draft": true, "prerelease": false },
              { "tag_name": "v1.0.0-rc1", "draft": false, "prerelease": true },
              { "tag_name": "v0.9.0", "draft": false, "prerelease": false }
            ]
            """;

        Assert.Equal("v1.0.0-rc1", GitHubReleaseClient.SelectTagFromReleaseList(json, "o/r"));
    }

    [Fact]
    public void SelectTagFromReleaseList_ThrowsWhenNoPublishedRelease()
    {
        Assert.Throws<InvalidOperationException>(
            () => GitHubReleaseClient.SelectTagFromReleaseList("[]", "o/r"));
        Assert.Throws<InvalidOperationException>(
            () => GitHubReleaseClient.SelectTagFromReleaseList(
                """[{ "tag_name": "v1.0.0", "draft": true }]""", "o/r"));
    }

    [Fact]
    public void SelectTagFromReleaseList_SkipsEntriesWithoutTagName()
    {
        const string json = """
            [
              { "draft": false },
              { "tag_name": "  v1.1.0  ", "draft": false }
            ]
            """;

        Assert.Equal("v1.1.0", GitHubReleaseClient.SelectTagFromReleaseList(json, "o/r"));
    }

    [Fact]
    public void SelectTagFromLatestRelease_ReturnsTrimmedTag()
    {
        Assert.Equal("v3.2.1", GitHubReleaseClient.SelectTagFromLatestRelease(
            """{ "tag_name": " v3.2.1 " }""", "o/r"));
    }

    [Fact]
    public void SelectTagFromLatestRelease_ThrowsOnMissingTagName()
    {
        Assert.Throws<InvalidOperationException>(
            () => GitHubReleaseClient.SelectTagFromLatestRelease("{}", "o/r"));
    }
}
