namespace AurSync.Updater;

internal static class PublishPlanBuilder
{
    private static readonly char[] DisallowedPlanChars = ['\t', '\r', '\n'];

    internal static async Task<IReadOnlyList<PublishPlanItem>> BuildPublishPlanAsync(
        DirectoryInfo packagesRoot,
        FileInfo changedFile,
        CancellationToken cancellationToken)
    {
        var changedPackages = await ReadChangedPackageNamesAsync(changedFile, cancellationToken);
        var planItems = new List<PublishPlanItem>();

        foreach (var packageName in changedPackages)
        {
            var packageDir = new DirectoryInfo(Path.Combine(packagesRoot.FullName, packageName));
            if (!packageDir.Exists)
            {
                throw new InvalidOperationException($"Changed package directory not found: {packageName}");
            }

            var cfgPath = new FileInfo(Path.Combine(packageDir.FullName, "updater.yaml"));
            if (!cfgPath.Exists)
            {
                throw new InvalidOperationException($"Changed package '{packageName}' is missing updater.yaml");
            }

            var pkgbuildPath = new FileInfo(Path.Combine(packageDir.FullName, "PKGBUILD"));
            if (!pkgbuildPath.Exists)
            {
                throw new InvalidOperationException($"Changed package '{packageName}' is missing PKGBUILD");
            }

            var cfg = await ConfigReader.ReadConfigAsync(cfgPath, cancellationToken);
            var aurPackage = string.IsNullOrWhiteSpace(cfg.AurPackage) ? packageName : cfg.AurPackage.Trim();

            var pkgver = await PkgbuildParser.ReadAssignmentAsync(pkgbuildPath, "pkgver", cancellationToken);
            if (string.IsNullOrWhiteSpace(pkgver))
            {
                throw new InvalidOperationException($"PKGBUILD for '{packageName}' is missing pkgver assignment");
            }

            var pkgrel = await PkgbuildParser.ReadAssignmentAsync(pkgbuildPath, "pkgrel", cancellationToken);
            if (string.IsNullOrWhiteSpace(pkgrel))
            {
                throw new InvalidOperationException($"PKGBUILD for '{packageName}' is missing pkgrel assignment");
            }

            ValidatePublishPlanField(packageName, nameof(packageName));
            ValidatePublishPlanField(aurPackage, nameof(aurPackage));
            ValidatePublishPlanField(pkgver, nameof(pkgver));
            ValidatePublishPlanField(pkgrel, nameof(pkgrel));

            planItems.Add(new PublishPlanItem(packageName, aurPackage, pkgver, pkgrel));
        }

        return planItems;
    }

    internal static async Task<IReadOnlyList<string>> ReadChangedPackageNamesAsync(FileInfo changedFile, CancellationToken cancellationToken)
    {
        if (!changedFile.Exists)
        {
            return [];
        }

        var names = (await File.ReadAllLinesAsync(changedFile.FullName, cancellationToken))
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        return names;
    }

    internal static void ValidatePublishPlanField(string value, string fieldName)
    {
        if (value.IndexOfAny(DisallowedPlanChars) >= 0)
        {
            throw new InvalidOperationException($"Publish plan field '{fieldName}' contains unsupported control characters");
        }
    }

    internal static async Task WritePublishPlanFileAsync(FileInfo planFile, IReadOnlyList<PublishPlanItem> planItems, CancellationToken cancellationToken)
    {
        planFile.Directory?.Create();
        var content = planItems.Count == 0
            ? string.Empty
            : string.Join('\n', planItems.Select(item => $"{item.PackageName}\t{item.AurPackage}\t{item.Pkgver}\t{item.Pkgrel}")) + "\n";
        await File.WriteAllTextAsync(planFile.FullName, content, cancellationToken);
    }
}

internal sealed record PublishPlanItem(string PackageName, string AurPackage, string Pkgver, string Pkgrel);
