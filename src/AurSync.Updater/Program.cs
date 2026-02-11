using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace AurSync.Updater;

internal static class Program
{
    private static readonly IDeserializer ConfigDeserializer = new StaticDeserializerBuilder(new UpdaterYamlContext())
        .IgnoreUnmatchedProperties()
        .WithCaseInsensitivePropertyMatching()
        .Build();

    private static async Task<int> Main(string[] args)
    {
        return await RunAsync(args, CancellationToken.None);
    }

    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        UpdaterOptions options;
        try
        {
            options = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Log($"[error] {ex.Message}");
            return 2;
        }

        var packagesRoot = new DirectoryInfo(options.PackagesRoot);
        var changedFile = new FileInfo(options.ChangedFile);
        var packageDirs = GetPackageDirs(packagesRoot, options.PackageFilter);

        using var http = CreateGithubClient();

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
            var cfg = await ReadConfigAsync(cfgPath, cancellationToken);
            var enabled = GetEffectiveIsEnabled(cfg);
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

            var latestTag = await GetLatestReleaseTagAsync(http, cfg.Repo, cancellationToken);
            var latestPkgver = StripPrefix(latestTag, cfg.Prefix ?? string.Empty);
            var currentPkgver = await ReadAssignmentAsync(pkgbuildPath, "pkgver", cancellationToken);

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
                await WritePkgbuildVersionAsync(pkgbuildPath, latestPkgver, cancellationToken);
                await RefreshPkgMetadataAsync(packageDir, cancellationToken);
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

    private static UpdaterOptions ParseArgs(string[] args)
    {
        var options = new UpdaterOptions
        {
            PackagesRoot = "packages",
            ChangedFile = ".changed-packages",
            PackageFilter = string.Empty,
            DryRun = false,
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--packages-root":
                    options.PackagesRoot = ReadValue(args, ref i, arg);
                    break;
                case "--changed-file":
                    options.ChangedFile = ReadValue(args, ref i, arg);
                    break;
                case "--package-filter":
                    options.PackageFilter = ReadValue(args, ref i, arg);
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--max-concurrency":
                    var rawConcurrency = ReadValue(args, ref i, arg);
                    if (!int.TryParse(rawConcurrency, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedConcurrency) || parsedConcurrency <= 0)
                    {
                        throw new ArgumentException("--max-concurrency must be a positive integer");
                    }
                    options.MaxConcurrency = parsedConcurrency;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options;

        static string ReadValue(string[] allArgs, ref int index, string flag)
        {
            if (index + 1 >= allArgs.Length)
            {
                throw new ArgumentException($"Missing value for {flag}");
            }

            index++;
            return allArgs[index];
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

    private static List<DirectoryInfo> GetPackageDirs(DirectoryInfo packagesRoot, string packageFilter)
    {
        if (!packagesRoot.Exists)
        {
            return new List<DirectoryInfo>();
        }

        var dirs = packagesRoot.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(packageFilter))
        {
            return dirs;
        }

        return dirs.Where(d => string.Equals(d.Name, packageFilter, StringComparison.Ordinal)).ToList();
    }

    private static HttpClient CreateGithubClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("aur-sync-hub", "1.0"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task<UpdaterConfig> ReadConfigAsync(FileInfo path, CancellationToken cancellationToken)
    {
        try
        {
            var yaml = await File.ReadAllTextAsync(path.FullName, cancellationToken);
            var parsed = ConfigDeserializer.Deserialize<UpdaterConfig>(yaml);
            return parsed ?? new UpdaterConfig();
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException($"Invalid YAML in {path.FullName}: {ex.Message}", ex);
        }
    }

    private static bool GetEffectiveIsEnabled(UpdaterConfig config)
    {
        return config.IsEnabled ?? true;
    }

    private static async Task<string> GetLatestReleaseTagAsync(HttpClient http, string repo, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{repo}/releases/latest";

        using var response = await http.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"No latest release found for {repo}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub API error for {repo}: HTTP {(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim());
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"No valid tag_name in latest release for {repo}");
        }

        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException($"No valid tag_name in latest release for {repo}");
        }

        return tag.Trim();
    }

    private static string StripPrefix(string tag, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return tag;
        }

        if (tag.StartsWith(prefix, StringComparison.Ordinal))
        {
            return tag[prefix.Length..];
        }

        throw new InvalidOperationException(
            $"Release tag '{tag}' does not start with configured prefix '{prefix}'");
    }

    private static async Task<string?> ReadAssignmentAsync(FileInfo file, string key, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(file.FullName, cancellationToken);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var match = AssignmentRegexHolder.Instance.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (!string.Equals(match.Groups[2].Value, key, StringComparison.Ordinal))
            {
                continue;
            }

            var value = match.Groups[3].Value.Trim();
            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    return value[1..^1];
                }
            }

            return value;
        }

        return null;
    }

    private static async Task WritePkgbuildVersionAsync(FileInfo file, string pkgver, CancellationToken cancellationToken)
    {
        var lines = (await File.ReadAllLinesAsync(file.FullName, cancellationToken)).ToList();
        var changedVer = ReplaceAssignment(lines, "pkgver", pkgver);
        var changedRel = ReplaceAssignment(lines, "pkgrel", "1");

        if (!changedVer && !changedRel)
        {
            return;
        }

        await File.WriteAllTextAsync(file.FullName, string.Join('\n', lines) + "\n", cancellationToken);
    }

    private static bool ReplaceAssignment(List<string> lines, string key, string newValue)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var match = AssignmentRegexHolder.Instance.Match(raw);
            if (!match.Success)
            {
                continue;
            }

            if (!string.Equals(match.Groups[2].Value, key, StringComparison.Ordinal))
            {
                continue;
            }

            var oldRaw = match.Groups[3].Value.Trim();
            var quote = "";
            if (oldRaw.Length >= 2)
            {
                var first = oldRaw[0];
                var last = oldRaw[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    quote = first.ToString();
                }
            }

            var updated = $"{match.Groups[1].Value}{key}={quote}{newValue}{quote}";
            var changed = !string.Equals(raw, updated, StringComparison.Ordinal);
            lines[i] = updated;
            return changed;
        }

        throw new InvalidOperationException($"Unable to find assignment for '{key}'");
    }

    private static async Task RefreshPkgMetadataAsync(DirectoryInfo packageDir, CancellationToken cancellationToken)
    {
        await RunProcessAsync("updpkgsums", Array.Empty<string>(), packageDir, captureStdout: false, cancellationToken);
        var srcinfo = await RunProcessAsync("makepkg", new[] { "--printsrcinfo" }, packageDir, captureStdout: true, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(packageDir.FullName, ".SRCINFO"), srcinfo, cancellationToken);
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        DirectoryInfo workingDir,
        bool captureStdout,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var details = string.Join("\n", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
            throw new InvalidOperationException($"Command failed: {fileName} (exit {process.ExitCode})\n{details}".Trim());
        }

        return captureStdout ? stdout : string.Empty;
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

    private sealed class UpdaterOptions
    {
        public required string PackagesRoot { get; set; }
        public required string ChangedFile { get; set; }
        public required string PackageFilter { get; set; }
        public bool DryRun { get; set; }
        public int? MaxConcurrency { get; set; }
    }

    private enum PackageOutcome
    {
        Skip,
        Ok,
        Changed,
        Error,
    }

    private sealed record PackageResult(string PackageName, PackageOutcome Outcome, bool Changed, string Message)
    {
        public static PackageResult Skip(string packageName, string message) =>
            new(packageName, PackageOutcome.Skip, false, message);

        public static PackageResult Ok(string packageName, string message) =>
            new(packageName, PackageOutcome.Ok, false, message);

        public static PackageResult ChangedPackage(string packageName, string message) =>
            new(packageName, PackageOutcome.Changed, true, message);

        public static PackageResult ErrorResult(string packageName, string message) =>
            new(packageName, PackageOutcome.Error, false, message);
    }

    private static class AssignmentRegexHolder
    {
        public static readonly Regex Instance = new("^(\\s*)([A-Za-z_][A-Za-z0-9_]*)=(.*)$", RegexOptions.Compiled);
    }
}
