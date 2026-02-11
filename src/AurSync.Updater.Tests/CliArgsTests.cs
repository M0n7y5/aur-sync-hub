namespace AurSync.Updater.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_DefaultValues()
    {
        var options = CliArgs.Parse([]);

        Assert.Equal("packages", options.PackagesRoot);
        Assert.Equal(".changed-packages", options.ChangedFile);
        Assert.Equal(string.Empty, options.ChangedPathsFile);
        Assert.Equal(".publish-plan", options.PublishPlanFile);
        Assert.Equal(string.Empty, options.PackageFilter);
        Assert.False(options.DryRun);
        Assert.False(options.DiscoverPackagesJson);
        Assert.False(options.BuildPublishPlan);
        Assert.Null(options.MaxConcurrency);
    }

    [Fact]
    public void Parse_AllFlags()
    {
        var options = CliArgs.Parse([
            "--packages-root", "/custom/root",
            "--changed-file", "/custom/changed",
            "--changed-paths-file", "/custom/paths",
            "--publish-plan-file", "/custom/plan",
            "--package-filter", "my-package",
            "--dry-run",
            "--max-concurrency", "4",
        ]);

        Assert.Equal("/custom/root", options.PackagesRoot);
        Assert.Equal("/custom/changed", options.ChangedFile);
        Assert.Equal("/custom/paths", options.ChangedPathsFile);
        Assert.Equal("/custom/plan", options.PublishPlanFile);
        Assert.Equal("my-package", options.PackageFilter);
        Assert.True(options.DryRun);
        Assert.Equal(4, options.MaxConcurrency);
    }

    [Fact]
    public void Parse_DiscoverPackagesJsonFlag()
    {
        var options = CliArgs.Parse(["--discover-packages-json"]);

        Assert.True(options.DiscoverPackagesJson);
    }

    [Fact]
    public void Parse_BuildPublishPlanFlag()
    {
        var options = CliArgs.Parse(["--build-publish-plan"]);

        Assert.True(options.BuildPublishPlan);
    }

    [Fact]
    public void Parse_ThrowsOnUnknownArgument()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(["--unknown-flag"]));

        Assert.Contains("Unknown argument", ex.Message);
    }

    [Fact]
    public void Parse_ThrowsOnMissingValue()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(["--packages-root"]));

        Assert.Contains("Missing value", ex.Message);
    }

    [Fact]
    public void Parse_ThrowsOnInvalidMaxConcurrency()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(["--max-concurrency", "abc"]));

        Assert.Contains("positive integer", ex.Message);
    }

    [Fact]
    public void Parse_ThrowsOnZeroMaxConcurrency()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(["--max-concurrency", "0"]));

        Assert.Contains("positive integer", ex.Message);
    }

    [Fact]
    public void Parse_ThrowsOnNegativeMaxConcurrency()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliArgs.Parse(["--max-concurrency", "-1"]));

        Assert.Contains("positive integer", ex.Message);
    }
}
