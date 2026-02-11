namespace AurSync.Updater.Tests;

public class PkgbuildParserTests
{
    #region StripQuotes

    [Theory]
    [InlineData("\"hello\"", "hello", "\"")]
    [InlineData("'hello'", "hello", "'")]
    [InlineData("hello", "hello", "")]
    [InlineData("\"\"", "", "\"")]
    [InlineData("''", "", "'")]
    [InlineData("a", "a", "")]
    [InlineData("", "", "")]
    [InlineData("\"mismatched'", "\"mismatched'", "")]
    [InlineData("'mismatched\"", "'mismatched\"", "")]
    public void StripQuotes_ReturnsExpected(string input, string expectedValue, string expectedQuote)
    {
        var (value, quote) = PkgbuildParser.StripQuotes(input);
        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedQuote, quote);
    }

    #endregion

    #region ReadAssignmentAsync

    [Fact]
    public async Task ReadAssignmentAsync_ReadsUnquotedValue()
    {
        var file = await WriteTempPkgbuild("pkgver=1.2.3\npkgrel=1\n");

        var result = await PkgbuildParser.ReadAssignmentAsync(file, "pkgver", CancellationToken.None);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task ReadAssignmentAsync_ReadsDoubleQuotedValue()
    {
        var file = await WriteTempPkgbuild("pkgver=\"1.2.3\"\npkgrel=1\n");

        var result = await PkgbuildParser.ReadAssignmentAsync(file, "pkgver", CancellationToken.None);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task ReadAssignmentAsync_ReadsSingleQuotedValue()
    {
        var file = await WriteTempPkgbuild("pkgver='1.2.3'\npkgrel=1\n");

        var result = await PkgbuildParser.ReadAssignmentAsync(file, "pkgver", CancellationToken.None);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task ReadAssignmentAsync_SkipsComments()
    {
        var file = await WriteTempPkgbuild("# pkgver=old\npkgver=1.2.3\n");

        var result = await PkgbuildParser.ReadAssignmentAsync(file, "pkgver", CancellationToken.None);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task ReadAssignmentAsync_SkipsBlankLines()
    {
        var file = await WriteTempPkgbuild("\n\npkgver=1.2.3\n\n");

        var result = await PkgbuildParser.ReadAssignmentAsync(file, "pkgver", CancellationToken.None);

        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public async Task ReadAssignmentAsync_ReturnsNullForMissingKey()
    {
        var file = await WriteTempPkgbuild("pkgrel=1\n");

        var result = await PkgbuildParser.ReadAssignmentAsync(file, "pkgver", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAssignmentAsync_HandlesLeadingWhitespace()
    {
        var file = await WriteTempPkgbuild("  pkgver=1.2.3\n");

        var result = await PkgbuildParser.ReadAssignmentAsync(file, "pkgver", CancellationToken.None);

        Assert.Equal("1.2.3", result);
    }

    #endregion

    #region ReplaceAssignment

    [Fact]
    public void ReplaceAssignment_ReplacesUnquotedValue()
    {
        var lines = new List<string> { "pkgver=1.0.0", "pkgrel=1" };

        var changed = PkgbuildParser.ReplaceAssignment(lines, "pkgver", "2.0.0");

        Assert.True(changed);
        Assert.Equal("pkgver=2.0.0", lines[0]);
    }

    [Fact]
    public void ReplaceAssignment_PreservesDoubleQuotes()
    {
        var lines = new List<string> { "pkgver=\"1.0.0\"", "pkgrel=1" };

        var changed = PkgbuildParser.ReplaceAssignment(lines, "pkgver", "2.0.0");

        Assert.True(changed);
        Assert.Equal("pkgver=\"2.0.0\"", lines[0]);
    }

    [Fact]
    public void ReplaceAssignment_PreservesSingleQuotes()
    {
        var lines = new List<string> { "pkgver='1.0.0'", "pkgrel=1" };

        var changed = PkgbuildParser.ReplaceAssignment(lines, "pkgver", "2.0.0");

        Assert.True(changed);
        Assert.Equal("pkgver='2.0.0'", lines[0]);
    }

    [Fact]
    public void ReplaceAssignment_PreservesLeadingWhitespace()
    {
        var lines = new List<string> { "  pkgver=1.0.0" };

        var changed = PkgbuildParser.ReplaceAssignment(lines, "pkgver", "2.0.0");

        Assert.True(changed);
        Assert.Equal("  pkgver=2.0.0", lines[0]);
    }

    [Fact]
    public void ReplaceAssignment_ReturnsFalseWhenValueUnchanged()
    {
        var lines = new List<string> { "pkgver=1.0.0" };

        var changed = PkgbuildParser.ReplaceAssignment(lines, "pkgver", "1.0.0");

        Assert.False(changed);
    }

    [Fact]
    public void ReplaceAssignment_ThrowsWhenKeyNotFound()
    {
        var lines = new List<string> { "pkgrel=1" };

        Assert.Throws<InvalidOperationException>(() =>
            PkgbuildParser.ReplaceAssignment(lines, "pkgver", "2.0.0"));
    }

    [Fact]
    public void ReplaceAssignment_SkipsCommentedLines()
    {
        var lines = new List<string> { "# pkgver=old", "pkgver=1.0.0" };

        var changed = PkgbuildParser.ReplaceAssignment(lines, "pkgver", "2.0.0");

        Assert.True(changed);
        Assert.Equal("# pkgver=old", lines[0]);
        Assert.Equal("pkgver=2.0.0", lines[1]);
    }

    #endregion

    #region Helpers

    private static async Task<FileInfo> WriteTempPkgbuild(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pkgbuild-test-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, content);
        return new FileInfo(path);
    }

    #endregion
}
