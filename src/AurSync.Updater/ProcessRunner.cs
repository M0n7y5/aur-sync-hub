using System.Diagnostics;

namespace AurSync.Updater;

internal static class ProcessRunner
{
    internal static async Task<string> RunAsync(
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
            var outputs = new[] { stdout, stderr };
            var details = string.Join("\n", outputs.Where(s => !string.IsNullOrWhiteSpace(s)));
            throw new InvalidOperationException($"Command failed: {fileName} (exit {process.ExitCode})\n{details}".Trim());
        }

        return captureStdout ? stdout : string.Empty;
    }
}
