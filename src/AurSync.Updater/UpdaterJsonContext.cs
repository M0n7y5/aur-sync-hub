using System.Text.Json.Serialization;

namespace AurSync.Updater;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(string[]))]
internal sealed partial class UpdaterJsonContext : JsonSerializerContext
{
}
