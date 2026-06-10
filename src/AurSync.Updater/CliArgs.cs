using System.Globalization;

namespace AurSync.Updater;

internal sealed class UpdaterOptions
{
    public string PackagesRoot { get; set; } = "packages";
    public string ChangedFile { get; set; } = ".changed-packages";
    public string ChangedPathsFile { get; set; } = string.Empty;
    public string PublishPlanFile { get; set; } = ".publish-plan";
    public string PackageFilter { get; set; } = string.Empty;
    public string PrintVerifyCommands { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public bool DiscoverPackagesJson { get; set; }
    public bool BuildPublishPlan { get; set; }
    public int? MaxConcurrency { get; set; }
}

internal static class CliArgs
{
    internal static UpdaterOptions Parse(string[] args)
    {
        var options = new UpdaterOptions();

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
                case "--changed-paths-file":
                    options.ChangedPathsFile = ReadValue(args, ref i, arg);
                    break;
                case "--publish-plan-file":
                    options.PublishPlanFile = ReadValue(args, ref i, arg);
                    break;
                case "--package-filter":
                    options.PackageFilter = ReadValue(args, ref i, arg);
                    break;
                case "--print-verify-commands":
                    options.PrintVerifyCommands = ReadValue(args, ref i, arg);
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--discover-packages-json":
                    options.DiscoverPackagesJson = true;
                    break;
                case "--build-publish-plan":
                    options.BuildPublishPlan = true;
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
}
