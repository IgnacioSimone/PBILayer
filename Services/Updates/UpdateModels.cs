namespace PBIExplorer.Services.Updates;

/// <summary>Una versión publicada: qué número es, qué trae y de dónde bajar el instalador y su hash.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Notes,
    string PageUrl,
    string? InstallerName,
    string? InstallerUrl,
    string? ChecksumUrl)
{
    /// <summary>Solo se instala solo si hay instalador Y hash para verificarlo; si no, se manda a la página.</summary>
    public bool CanInstall => InstallerUrl is not null && ChecksumUrl is not null;
}

public enum UpdateStatus
{
    UpToDate,
    Available,
    /// <summary>No hay ninguna versión publicada (o el repositorio no es público).</summary>
    NoReleases,
    Failed
}

public sealed record UpdateCheckResult(UpdateStatus Status, ReleaseInfo? Release = null, string? Error = null);

public interface IUpdateFeed
{
    /// <summary>La última versión publicada, o null si no hay ninguna.</summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct);
}

/// <summary>Sin ninguna versión publicada: lo que se usa cuando no se configuró un origen real (por ejemplo, en los tests).</summary>
public sealed class NullUpdateFeed : IUpdateFeed
{
    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct) => Task.FromResult<ReleaseInfo?>(null);
}

public static class UpdateChecker
{
    public static async Task<UpdateCheckResult> CheckAsync(IUpdateFeed feed, Version current, CancellationToken ct = default)
    {
        try
        {
            var latest = await feed.GetLatestAsync(ct);
            if (latest is null) return new UpdateCheckResult(UpdateStatus.NoReleases);

            return latest.Version > current
                ? new UpdateCheckResult(UpdateStatus.Available, latest)
                : new UpdateCheckResult(UpdateStatus.UpToDate, latest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateStatus.Failed, Error: ex.Message);
        }
    }
}
