using PBIExplorer.Infrastructure;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace PBIExplorer.Services.Updates;

/// <summary>Baja el instalador de una versión nueva y comprueba su SHA-256 contra el que se publicó junto a él.
/// Nunca devuelve un archivo que no coincida: si la descarga se corrompió o el hash no está, se borra y falla.
/// Importante: sin firma de código (Authenticode), el hash protege contra descargas dañadas, no contra
/// una cuenta de GitHub comprometida; esa confianza la pone el repositorio.</summary>
public sealed class UpdateInstaller
{
    /// <summary>Solo se baja de GitHub (las descargas de releases redirigen a estos dominios).</summary>
    private static readonly string[] TrustedHosts =
    {
        "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "github-releases.githubusercontent.com"
    };

    private readonly HttpClient _http;
    private readonly string _directory;

    public UpdateInstaller(HttpClient http, string directory)
    {
        _http = http;
        _directory = directory;
    }

    public async Task<string> DownloadAndVerifyAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        if (!release.CanInstall || release.InstallerName is null)
            throw new InvalidOperationException(Loc.T("Install_NoInstallerPublished"));

        if (!IsTrustedUrl(release.InstallerUrl!) || !IsTrustedUrl(release.ChecksumUrl!))
            throw new InvalidOperationException(Loc.T("Install_UntrustedUrl"));

        // El nombre sale de la respuesta de red: nunca se usa como ruta tal cual.
        var fileName = Path.GetFileName(release.InstallerName);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("Install_BadName"));

        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, fileName);
        var partial = target + ".part";

        var expected = ParseChecksum(await _http.GetStringAsync(release.ChecksumUrl!, ct), fileName)
                       ?? throw new InvalidDataException(Loc.T("Install_BadChecksumFile"));

        try
        {
            using (var response = await _http.GetAsync(release.InstallerUrl!, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var destination = File.Create(partial);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total is > 0) progress?.Report((double)done / total.Value);
                }
            }

            var actual = await HashFileAsync(partial, ct);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Loc.T("Install_HashMismatch"));

            File.Move(partial, target, overwrite: true);
            progress?.Report(1);
            return target;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        TrustedHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>Acepta "hash" solo, o "hash  archivo.exe" / "hash *archivo.exe" (formato de sha256sum).</summary>
    public static string? ParseChecksum(string text, string fileName)
    {
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !IsSha256(parts[0])) continue;

            if (parts.Length == 1) return parts[0].ToLowerInvariant();

            var named = parts[1].Trim().TrimStart('*');
            if (string.Equals(named, fileName, StringComparison.OrdinalIgnoreCase)) return parts[0].ToLowerInvariant();
        }
        return null;
    }

    private static bool IsSha256(string text) => text.Length == 64 && text.All(Uri.IsHexDigit);

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* queda un .part en la carpeta de actualizaciones; se limpia en la próxima */ }
    }
}
