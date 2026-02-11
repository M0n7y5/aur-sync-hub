namespace AurSync.Updater.Tests;

public class PackageDiscoveryTests
{
    #region TryExtractPackageNameFromPath

    [Theory]
    [InlineData("packages/csharpier/PKGBUILD", true, "csharpier")]
    [InlineData("packages/plasticity-bin/.SRCINFO", true, "plasticity-bin")]
    [InlineData("packages/foo/updater.yaml", true, "foo")]
    [InlineData("packages/bar", true, "bar")]
    [InlineData("./packages/csharpier/PKGBUILD", true, "csharpier")]
    [InlineData("packages\\foo\\PKGBUILD", true, "foo")]
    [InlineData("src/AurSync.Updater/Program.cs", false, "")]
    [InlineData(".github/workflows/test.yml", false, "")]
    [InlineData("packages/", false, "")]
    [InlineData("", false, "")]
    [InlineData("  ", false, "")]
    [InlineData("packages/my-pkg/sub/deep/file.txt", true, "my-pkg")]
    public void TryExtractPackageNameFromPath_ReturnsExpected(string rawPath, bool expectedResult, string expectedName)
    {
        var result = PackageDiscovery.TryExtractPackageNameFromPath(rawPath, out var packageName);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedName, packageName);
    }

    #endregion

    #region GetPackageDirs

    [Fact]
    public void GetPackageDirs_ReturnsEmptyForNonExistentRoot()
    {
        var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}"));

        var result = PackageDiscovery.GetPackageDirs(root, string.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void GetPackageDirs_ReturnsAllDirsWhenNoFilter()
    {
        var root = CreateTempPackagesRoot("alpha", "beta", "gamma");

        var result = PackageDiscovery.GetPackageDirs(root, string.Empty);

        Assert.Equal(3, result.Count);
        Assert.Equal(["alpha", "beta", "gamma"], result.Select(d => d.Name).ToList());
    }

    [Fact]
    public void GetPackageDirs_FiltersToSinglePackage()
    {
        var root = CreateTempPackagesRoot("alpha", "beta", "gamma");

        var result = PackageDiscovery.GetPackageDirs(root, "beta");

        Assert.Single(result);
        Assert.Equal("beta", result[0].Name);
    }

    [Fact]
    public void GetPackageDirs_ReturnsEmptyWhenFilterMatchesNothing()
    {
        var root = CreateTempPackagesRoot("alpha", "beta");

        var result = PackageDiscovery.GetPackageDirs(root, "nonexistent");

        Assert.Empty(result);
    }

    #endregion

    #region HasPackageSyncInputs

    [Fact]
    public void HasPackageSyncInputs_ReturnsTrueWhenBothExist()
    {
        var dir = CreateTempDirWithFiles("updater.yaml", "PKGBUILD");

        Assert.True(PackageDiscovery.HasPackageSyncInputs(dir));
    }

    [Fact]
    public void HasPackageSyncInputs_ReturnsFalseWhenMissingPkgbuild()
    {
        var dir = CreateTempDirWithFiles("updater.yaml");

        Assert.False(PackageDiscovery.HasPackageSyncInputs(dir));
    }

    [Fact]
    public void HasPackageSyncInputs_ReturnsFalseWhenMissingUpdaterYaml()
    {
        var dir = CreateTempDirWithFiles("PKGBUILD");

        Assert.False(PackageDiscovery.HasPackageSyncInputs(dir));
    }

    #endregion

    #region Helpers

    private static DirectoryInfo CreateTempPackagesRoot(params string[] packageNames)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pkg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        foreach (var name in packageNames)
        {
            Directory.CreateDirectory(Path.Combine(root, name));
        }
        return new DirectoryInfo(root);
    }

    private static DirectoryInfo CreateTempDirWithFiles(params string[] fileNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pkg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var name in fileNames)
        {
            File.WriteAllText(Path.Combine(dir, name), "");
        }
        return new DirectoryInfo(dir);
    }

    #endregion
}
