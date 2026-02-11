namespace AurSync.Updater;

internal static class PackageDiscovery
{
    internal static List<DirectoryInfo> GetPackageDirs(DirectoryInfo packagesRoot, string packageFilter)
    {
        if (!packagesRoot.Exists)
        {
            return [];
        }

        var dirs = packagesRoot.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(packageFilter))
        {
            return dirs;
        }

        return dirs.Where(d => string.Equals(d.Name, packageFilter, StringComparison.Ordinal)).ToList();
    }

    internal static async Task<IReadOnlyList<string>> DiscoverPackagesForVerifyAsync(
        DirectoryInfo packagesRoot,
        string packageFilter,
        string changedPathsFile,
        CancellationToken cancellationToken)
    {
        var packageDirs = GetPackageDirs(packagesRoot, packageFilter);

        HashSet<string>? changedPackages = null;
        if (!string.IsNullOrWhiteSpace(changedPathsFile))
        {
            changedPackages = await ReadChangedPackageNamesFromPathsFileAsync(new FileInfo(changedPathsFile), cancellationToken);
            if (changedPackages.Count == 0)
            {
                return [];
            }
        }

        var discovered = packageDirs
            .Where(HasPackageSyncInputs)
            .Where(d => changedPackages is null || changedPackages.Contains(d.Name))
            .Select(d => d.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return discovered;
    }

    internal static bool TryExtractPackageNameFromPath(string rawPath, out string packageName)
    {
        packageName = string.Empty;
        var normalized = rawPath.Trim().Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        const string prefix = "packages/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = normalized[prefix.Length..];
        if (string.IsNullOrWhiteSpace(rest))
        {
            return false;
        }

        var slashIndex = rest.IndexOf('/');
        packageName = slashIndex >= 0 ? rest[..slashIndex] : rest;
        return packageName.Length > 0;
    }

    internal static bool HasPackageSyncInputs(DirectoryInfo packageDir)
    {
        var cfgPath = new FileInfo(Path.Combine(packageDir.FullName, "updater.yaml"));
        var pkgbuildPath = new FileInfo(Path.Combine(packageDir.FullName, "PKGBUILD"));
        return cfgPath.Exists && pkgbuildPath.Exists;
    }

    private static async Task<HashSet<string>> ReadChangedPackageNamesFromPathsFileAsync(FileInfo changedPathsFile, CancellationToken cancellationToken)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);
        if (!changedPathsFile.Exists)
        {
            return packages;
        }

        var lines = await File.ReadAllLinesAsync(changedPathsFile.FullName, cancellationToken);
        foreach (var raw in lines)
        {
            if (TryExtractPackageNameFromPath(raw, out var packageName))
            {
                packages.Add(packageName);
            }
        }

        return packages;
    }
}
