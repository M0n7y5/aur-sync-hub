using System.Text.RegularExpressions;

namespace AurSync.Updater;

internal static partial class PkgbuildParser
{
    [GeneratedRegex(@"^(\s*)([A-Za-z_][A-Za-z0-9_]*)=(.*)$")]
    private static partial Regex AssignmentRegex();

    internal static async Task<string?> ReadAssignmentAsync(FileInfo file, string key, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(file.FullName, cancellationToken);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var match = AssignmentRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (!string.Equals(match.Groups[2].Value, key, StringComparison.Ordinal))
            {
                continue;
            }

            var value = match.Groups[3].Value.Trim();
            return StripQuotes(value).Value;
        }

        return null;
    }

    internal static bool ReplaceAssignment(List<string> lines, string key, string newValue)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var match = AssignmentRegex().Match(raw);
            if (!match.Success)
            {
                continue;
            }

            if (!string.Equals(match.Groups[2].Value, key, StringComparison.Ordinal))
            {
                continue;
            }

            var (_, quote) = StripQuotes(match.Groups[3].Value.Trim());

            var updated = $"{match.Groups[1].Value}{key}={quote}{newValue}{quote}";
            var changed = !string.Equals(raw, updated, StringComparison.Ordinal);
            lines[i] = updated;
            return changed;
        }

        throw new InvalidOperationException($"Unable to find assignment for '{key}'");
    }

    internal static async Task WritePkgbuildVersionAsync(FileInfo file, string pkgver, CancellationToken cancellationToken)
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

    internal static async Task RefreshPkgMetadataAsync(DirectoryInfo packageDir, CancellationToken cancellationToken)
    {
        await ProcessRunner.RunAsync("updpkgsums", [], packageDir, captureStdout: false, cancellationToken);
        var srcinfo = await ProcessRunner.RunAsync("makepkg", ["--printsrcinfo"], packageDir, captureStdout: true, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(packageDir.FullName, ".SRCINFO"), srcinfo, cancellationToken);
    }

    /// <summary>
    /// Strips surrounding matching quotes (single or double) from a value.
    /// Returns the stripped value and the quote character used (empty string if none).
    /// </summary>
    internal static (string Value, string Quote) StripQuotes(string raw)
    {
        if (raw.Length >= 2)
        {
            var first = raw[0];
            var last = raw[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return (raw[1..^1], first.ToString());
            }
        }

        return (raw, string.Empty);
    }
}
