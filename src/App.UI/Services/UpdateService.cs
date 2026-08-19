using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Banog.UI.Services;

/// <summary>Une version plus récente que l'installée a été publiée.</summary>
public sealed record UpdateInfo(string Version, string Tag, string HtmlUrl);

/// <summary>La parcelle de la réponse de l'API GitHub « releases/latest » qui nous sert.</summary>
internal sealed class GitHubRelease
{
    public string? TagName { get; set; }
    public string? HtmlUrl { get; set; }
}

/// <summary>
/// Contexte System.Text.Json généré à la compilation : aucune réflexion à l'exécution,
/// indispensable pour Native AOT (le projet est publié en trim intégral).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;

/// <summary>
/// Interroge l'API publique de GitHub pour savoir si une version plus récente que celle
/// installée est publiée. Appel anonyme, sans jeton, sans blocage du démarrage. Tout
/// échec (hors ligne, limite de débit, réponse inattendue) est traité comme « pas de
/// mise à jour » : une vérification ratée ne doit jamais gêner l'utilisateur.
/// </summary>
public sealed class UpdateService
{
    private const string Repository = "NetSo8/Banog";
    private static readonly Uri LatestUrl = new($"https://api.github.com/repos/{Repository}/releases/latest");

    private readonly HttpClient _http = new();

    public UpdateService()
    {
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new("Banog", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"));
    }

    /// <summary>Version de l'exécutable en cours, telle que déclarée au build.</summary>
    public Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(LatestUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.GitHubRelease);
            if (release is not { TagName: { } tag }) return null;

            var latest = ParseVersion(tag);
            if (latest is null || latest <= CurrentVersion) return null;

            return new UpdateInfo(latest.ToString(), tag, release.HtmlUrl ?? LatestUrl.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var cleaned = tag.TrimStart('v', 'V');
        return Version.TryParse(cleaned, out var version) ? version : null;
    }
}
