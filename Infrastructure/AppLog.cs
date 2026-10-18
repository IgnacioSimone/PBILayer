using System.IO;
using PBIExplorer.Services;

namespace PBIExplorer.Infrastructure;

/// <summary>Log best-effort en %LOCALAPPDATA%\PBIContext. Nunca debe romper la app.</summary>
public static class AppLog
{
    public static void Write(string message)
    {
        try { File.AppendAllText(AppPaths.LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n"); }
        catch { /* el log es best-effort */ }
    }
}
