namespace AurSync.Updater.Tests;

public class ConfigReaderTests
{
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
