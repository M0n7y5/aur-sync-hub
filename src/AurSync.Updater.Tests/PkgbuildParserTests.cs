namespace AurSync.Updater.Tests;

public class PkgbuildParserTests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("aursync-tests-");

    public void Dispose()
    {
        _tempRoot.Delete(recursive: true);
    }

    #region ParseValue

    [Theory]
    [InlineData("\"hello\"", "hello", "\"", "")]
    [InlineData("'hello'", "hello", "'", "")]
    [InlineData("hello", "hello", "", "")]
    [InlineData("\"\"", "", "\"", "")]
    [InlineData("''", "", "'", "")]
    [InlineData("a", "a", "", "")]
    [InlineData("", "", "", "")]
    [InlineData("\"mismatched'", "\"mismatched'", "", "")]
    [InlineData("'mismatched\"", "'mismatched\"", "", "")]
    [InlineData("1.2.3 # note", "1.2.3", "", " # note")]
    [InlineData("\"1.2.3\" # note", "1.2.3", "\"", " # note")]
    [InlineData("'1.2.3' # note", "1.2.3", "'", " # note")]
    [InlineData("1.2.3#hash", "1.2.3#hash", "", "")]
    public void ParseValue_ReturnsExpected(string input, string expectedValue, string expectedQuote, string expectedSuffix)
    {
        var (value, quote, suffix) = PkgbuildParser.ParseValue(input);
        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedQuote, quote);
        Assert.Equal(expectedSuffix, suffix);
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

    [Fact]
    public async Task ReadAssignmentAsync_StripsTrailingComment()
    {
        var file = await WriteTempPkgbuild("pkgver=1.2.3 # bumped manually\n");

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

    [Fact]
    public void ReplaceAssignment_PreservesTrailingComment()
    {
        var lines = new List<string> { "pkgver=1.0.0 # keep me" };

        var changed = PkgbuildParser.ReplaceAssignment(lines, "pkgver", "2.0.0");

        Assert.True(changed);
        Assert.Equal("pkgver=2.0.0 # keep me", lines[0]);
    }

    #endregion

    #region Helpers

    private async Task<FileInfo> WriteTempPkgbuild(string content)
    {
        var path = Path.Combine(_tempRoot.FullName, $"pkgbuild-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, content);
        return new FileInfo(path);
    }

    #endregion
}
