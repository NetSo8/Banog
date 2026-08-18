using Banog.Core.Model;
using Banog.Core.Serialization;

namespace Banog.Core.Storage;

public interface IConfigurationStore
{
    string Location { get; }
    Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistance JSON du fichier de règles. Écriture atomique (fichier temporaire + remplacement)
/// pour ne jamais laisser une configuration tronquée derrière un crash.
/// </summary>
public sealed class JsonConfigurationStore(string? path = null) : IConfigurationStore
{
    public string Location { get; } = path ?? DefaultPath();

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Banog",
        "rules.json");

    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Location)) return new AppConfiguration();

        var json = await File.ReadAllTextAsync(Location, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return new AppConfiguration();

        return RulesJson.Deserialize(json) ?? new AppConfiguration();
    }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Location);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = RulesJson.Serialize(configuration);
        var temporary = Location + ".tmp";

        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);

        if (File.Exists(Location))
        {
            File.Replace(temporary, Location, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, Location);
        }
    }
}
