namespace AurSync.Updater.Tests;

public class PublishPlanBuilderTests
{
    [Fact]
    public void ValidatePublishPlanField_AcceptsNormalString()
    {
        // Should not throw
        PublishPlanBuilder.ValidatePublishPlanField("csharpier", "packageName");
    }

    [Fact]
    public void ValidatePublishPlanField_AcceptsVersionString()
    {
        PublishPlanBuilder.ValidatePublishPlanField("1.2.3", "pkgver");
    }

    [Theory]
    [InlineData("value\twith\ttabs")]
    [InlineData("value\nwith\nnewlines")]
    [InlineData("value\rwith\rreturns")]
    [InlineData("mixed\t\n\r")]
    public void ValidatePublishPlanField_ThrowsOnControlCharacters(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PublishPlanBuilder.ValidatePublishPlanField(value, "testField"));

        Assert.Contains("unsupported control characters", ex.Message);
    }

    [Fact]
    public async Task ReadChangedPackageNamesAsync_ReturnsEmptyForMissingFile()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}"));

        var result = await PublishPlanBuilder.ReadChangedPackageNamesAsync(file, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadChangedPackageNamesAsync_ReadsAndDeduplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"changed-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, "beta\nalpha\nbeta\n\nalpha\n");

        var result = await PublishPlanBuilder.ReadChangedPackageNamesAsync(new FileInfo(path), CancellationToken.None);

        Assert.Equal(["alpha", "beta"], result);
    }

    [Fact]
    public async Task WritePublishPlanFileAsync_WritesEmptyForNoPlan()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plan-{Guid.NewGuid():N}");

        await PublishPlanBuilder.WritePublishPlanFileAsync(new FileInfo(path), [], CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public async Task WritePublishPlanFileAsync_WritesTsvFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plan-{Guid.NewGuid():N}");
        var items = new List<PublishPlanItem>
        {
            new("csharpier", "csharpier", "1.0.0", "1"),
            new("plasticity-bin", "plasticity-bin", "2.0.0", "1"),
        };

        await PublishPlanBuilder.WritePublishPlanFileAsync(new FileInfo(path), items, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        Assert.Equal("csharpier\tcsharpier\t1.0.0\t1\nplasticity-bin\tplasticity-bin\t2.0.0\t1\n", content);
    }
}
