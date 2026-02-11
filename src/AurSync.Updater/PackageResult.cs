namespace AurSync.Updater;

internal enum PackageOutcome
{
    Skip,
    Ok,
    Changed,
    Error,
}

internal sealed record PackageResult(string PackageName, PackageOutcome Outcome, bool Changed, string Message)
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
