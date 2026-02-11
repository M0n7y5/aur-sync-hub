using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace AurSync.Updater;

internal static class ConfigReader
{
    private static readonly IDeserializer ConfigDeserializer = new StaticDeserializerBuilder(new UpdaterYamlContext())
        .IgnoreUnmatchedProperties()
        .WithCaseInsensitivePropertyMatching()
        .Build();

    internal static async Task<UpdaterConfig> ReadConfigAsync(FileInfo path, CancellationToken cancellationToken)
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

    internal static bool GetEffectiveIsEnabled(UpdaterConfig config)
    {
        return config.IsEnabled ?? true;
    }
}
