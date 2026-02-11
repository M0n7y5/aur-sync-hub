using YamlDotNet.Serialization;

namespace AurSync.Updater;

[YamlSerializable]
public sealed class UpdaterConfig
{
    [YamlMember(Alias = "source")]
    public string? Source { get; set; }

    [YamlMember(Alias = "repo")]
    public string? Repo { get; set; }

    [YamlMember(Alias = "prefix")]
    public string? Prefix { get; set; }

    [YamlMember(Alias = "aur_package")]
    public string? AurPackage { get; set; }

    [YamlMember(Alias = "isEnabled")]
    public bool? IsEnabled { get; set; }

    [YamlMember(Alias = "verify_commands")]
    public List<string>? VerifyCommands { get; set; }
}
