using PBIExplorer.Infrastructure;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PBIExplorer.Services.Updates;

/// <summary>Consulta la última "release" publicada de un repositorio de GitHub. Es la única conexión de red
/// de toda la aplicación y solo ocurre si el usuario la activa: un GET a la API pública, sin enviar datos
/// del modelo, del tablero ni de la máquina (solo el nombre y la versión de la app en el User-Agent).</summary>
public sealed class GitHubUpdateFeed : IUpdateFeed
{
    private readonly HttpClient _http;
    private readonly string _url;

    public GitHubUpdateFeed(HttpClient http, string owner, string repository)
    {
        _http = http;
        _url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/latest";
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        using var response = await _http.SendAsync(request, ct);

        // 404 = sin releases publicadas, o repositorio privado (la API no distingue y no hace falta que lo haga).
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-RateLimit-Remaining", out var left) &&
                left.FirstOrDefault() == "0")
                throw new HttpRequestException(Loc.T("Install_RateLimit"));
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return Parse(json);
    }

    /// <summary>Convierte la respuesta de la API en una <see cref="ReleaseInfo"/>; null si es borrador, prerelease
    /// o su etiqueta no es un número de versión.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (Flag(root, "draft") || Flag(root, "prerelease")) return null;

        var tag = Text(root, "tag_name");
        if (!TryParseTag(tag, out var version)) return null;

        string? installerName = null, installerUrl = null, checksumUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                var url = Text(asset, "browser_download_url");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;

                if (name.StartsWith("PBILayer-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installerName = name;
                    installerUrl = url;
                }
            }

            if (installerName is not null)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (string.Equals(Text(asset, "name"), installerName + ".sha256", StringComparison.OrdinalIgnoreCase))
                        checksumUrl = Text(asset, "browser_download_url");
                }
            }
        }

        return new ReleaseInfo(version, tag, Text(root, "body"), Text(root, "html_url"), installerName, installerUrl, checksumUrl);
    }

    /// <summary>"v2.1.0" o "2.1" → 2.1.0.</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var numeric = tag.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(numeric, out var parsed)) return false;

        version = new Version(parsed.Major, Math.Max(parsed.Minor, 0), Math.Max(parsed.Build, 0));
        return true;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
