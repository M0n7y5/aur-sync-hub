using System.Collections.Concurrent;
using System.Text.Json;

namespace AurSync.Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await RunAsync(args, CancellationToken.None);
    }

    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        UpdaterOptions options;
        try
        {
            options = CliArgs.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Log($"[error] {ex.Message}");
            return 2;
        }

        var packagesRoot = new DirectoryInfo(options.PackagesRoot);

        if (options.DiscoverPackagesJson)
        {
            var discoveredPackages = await PackageDiscovery.DiscoverPackagesForVerifyAsync(
                packagesRoot,
                options.PackageFilter,
                options.ChangedPathsFile,
                cancellationToken);
            var discoveredPackagesJson = JsonSerializer.Serialize(
                discoveredPackages.ToArray(),
                UpdaterJsonContext.Default.StringArray);
            Log(discoveredPackagesJson);
            return 0;
        }

        if (options.BuildPublishPlan)
        {
            var publishPlan = await PublishPlanBuilder.BuildPublishPlanAsync(
                packagesRoot,
                new FileInfo(options.ChangedFile),
                cancellationToken);
            var publishPlanFile = new FileInfo(options.PublishPlanFile);
            await PublishPlanBuilder.WritePublishPlanFileAsync(publishPlanFile, publishPlan, cancellationToken);
            Log($"Done. Publish plan entries: {publishPlan.Count}");
            return 0;
        }

        var changedFile = new FileInfo(options.ChangedFile);
        var packageDirs = PackageDiscovery.GetPackageDirs(packagesRoot, options.PackageFilter);

        using var http = GitHubReleaseClient.CreateGithubClient();

        var maxConcurrency = ResolveMaxConcurrency(options.MaxConcurrency);
        Log($"Using max concurrency: {maxConcurrency}");
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = cancellationToken,
        };

        var resultBag = new ConcurrentBag<PackageResult>();
        await Parallel.ForEachAsync(packageDirs, parallelOptions, async (packageDir, ct) =>
        {
            var result = await ProcessPackageAsync(packageDir, options, http, ct);
            resultBag.Add(result);
        });

        var results = resultBag.ToArray();

        foreach (var result in results.OrderBy(r => r.PackageName, StringComparer.Ordinal))
        {
            Log(result.Message);
        }

        var changedPackages = results
            .Where(r => r.Changed)
            .Select(r => r.PackageName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        await WriteChangedFileAsync(changedFile, changedPackages, cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.PackageFilter) && packageDirs.Count == 0)
        {
            Log($"[warn] package filter matched nothing: {options.PackageFilter}");
        }

        var errorCount = results.Count(r => r.Outcome == PackageOutcome.Error);
        if (errorCount > 0)
        {
            Log($"Completed with {errorCount} error(s)");
            return 1;
        }

        Log($"Done. Changed packages: {changedPackages.Count}");
        return 0;
    }

    private static async Task<PackageResult> ProcessPackageAsync(
        DirectoryInfo packageDir,
        UpdaterOptions options,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        var packageName = packageDir.Name;
        var cfgPath = new FileInfo(Path.Combine(packageDir.FullName, "updater.yaml"));
        var pkgbuildPath = new FileInfo(Path.Combine(packageDir.FullName, "PKGBUILD"));

        if (!cfgPath.Exists)
        {
            return PackageResult.Skip(packageName, $"[skip] {packageName}: missing updater.yaml");
        }

        if (!pkgbuildPath.Exists)
        {
            return PackageResult.Skip(packageName, $"[skip] {packageName}: missing PKGBUILD");
        }

        try
        {
            var cfg = await ConfigReader.ReadConfigAsync(cfgPath, cancellationToken);
            var enabled = ConfigReader.GetEffectiveIsEnabled(cfg);
            if (!enabled)
            {
                return PackageResult.Skip(packageName, $"[skip] {packageName}: disabled in updater.yaml (isEnabled: false)");
            }

            if (string.IsNullOrWhiteSpace(cfg.Source) || !string.Equals(cfg.Source, "github_release", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported source '{cfg.Source ?? string.Empty}'");
            }

            if (string.IsNullOrWhiteSpace(cfg.Repo) || !cfg.Repo.Contains('/'))
            {
                throw new InvalidOperationException("Config 'repo' must be in 'owner/repo' format");
            }

            var latestTag = await GitHubReleaseClient.GetLatestReleaseTagAsync(http, cfg.Repo, cancellationToken);
            var latestPkgver = GitHubReleaseClient.StripPrefix(latestTag, cfg.Prefix ?? string.Empty);
            var currentPkgver = await PkgbuildParser.ReadAssignmentAsync(pkgbuildPath, "pkgver", cancellationToken);

            if (string.IsNullOrWhiteSpace(currentPkgver))
            {
                throw new InvalidOperationException("PKGBUILD is missing pkgver assignment");
            }

            if (string.Equals(currentPkgver, latestPkgver, StringComparison.Ordinal))
            {
                return PackageResult.Ok(packageName, $"[ok] {packageName}: already up-to-date ({currentPkgver})");
            }

            var drySuffix = options.DryRun ? " (dry-run)" : string.Empty;
            var updateMessage = $"[update] {packageName}: {currentPkgver} -> {latestPkgver}{drySuffix}";

            if (!options.DryRun)
            {
                await PkgbuildParser.WritePkgbuildVersionAsync(pkgbuildPath, latestPkgver, cancellationToken);
                await PkgbuildParser.RefreshPkgMetadataAsync(packageDir, cancellationToken);
            }

            return PackageResult.ChangedPackage(packageName, updateMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PackageResult.ErrorResult(packageName, $"[error] {packageName}: {ex.Message}");
        }
    }

    private static int ResolveMaxConcurrency(int? configured)
    {
        if (configured.HasValue)
        {
            return configured.Value;
        }

        return Math.Clamp(Environment.ProcessorCount, 2, 8);
    }

    private static async Task WriteChangedFileAsync(FileInfo changedFile, IReadOnlyList<string> changedPackages, CancellationToken cancellationToken)
    {
        changedFile.Directory?.Create();
        var content = changedPackages.Count == 0
            ? string.Empty
            : string.Join('\n', changedPackages) + "\n";
        await File.WriteAllTextAsync(changedFile.FullName, content, cancellationToken);
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
    }
}
