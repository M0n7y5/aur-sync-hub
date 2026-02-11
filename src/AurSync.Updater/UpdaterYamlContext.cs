using YamlDotNet.Serialization;

namespace AurSync.Updater;

[YamlStaticContext]
[YamlSerializable(typeof(UpdaterConfig))]
public partial class UpdaterYamlContext : StaticContext
{
}
