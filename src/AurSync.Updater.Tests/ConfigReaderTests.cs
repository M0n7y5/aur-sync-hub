namespace AurSync.Updater.Tests;

public class ConfigReaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aursync-tests-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
    }

    [Fact]
    public async Task ReadConfigAsync_ParsesAllowPrerelease()
    {
        var path = Path.Combine(_root.FullName, "updater.yaml");
        await File.WriteAllTextAsync(path, "source: github_release\nrepo: o/r\nallow_prerelease: true\n");

        var cfg = await ConfigReader.ReadConfigAsync(new FileInfo(path), CancellationToken.None);

        Assert.True(cfg.AllowPrerelease);
    }

    [Fact]
    public async Task ReadConfigAsync_AllowPrereleaseDefaultsToNull()
    {
        var path = Path.Combine(_root.FullName, "updater.yaml");
        await File.WriteAllTextAsync(path, "source: github_release\nrepo: o/r\n");

        var cfg = await ConfigReader.ReadConfigAsync(new FileInfo(path), CancellationToken.None);

        Assert.Null(cfg.AllowPrerelease);
    }

    [Fact]
    public void GetVerifyCommands_ReturnsEmptyForNullList()
    {
        Assert.Empty(ConfigReader.GetVerifyCommands(new UpdaterConfig()));
    }

    [Fact]
    public void GetVerifyCommands_TrimsAndSkipsBlankEntries()
    {
        var cfg = new UpdaterConfig { VerifyCommands = ["  a --version  ", "", "   ", "b -v"] };

        Assert.Equal(["a --version", "b -v"], ConfigReader.GetVerifyCommands(cfg));
    }
}
