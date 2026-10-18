using PBIExplorer.Services;
using Xunit;

namespace PBIExplorer.Tests;

public class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pbilayer-paths-" + Guid.NewGuid().ToString("N"));

    private string Old => Path.Combine(_root, "old");
    private string New => Path.Combine(_root, "new");

    public AppPathsTests() => Directory.CreateDirectory(Old);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Preferences_are_copied_from_the_previous_folder_when_there_are_none_yet()
    {
        File.WriteAllText(Path.Combine(Old, "settings.json"), "{\"Theme\":\"Dark\"}");

        AppPaths.MigratePreferences(Old, New);

        Assert.Equal("{\"Theme\":\"Dark\"}", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Old, "settings.json")));   // copia: la vieja no se toca
    }

    [Fact]
    public void Existing_preferences_are_never_overwritten()
    {
        File.WriteAllText(Path.Combine(Old, "settings.json"), "viejas");
        Directory.CreateDirectory(New);
        File.WriteAllText(Path.Combine(New, "settings.json"), "nuevas");

        AppPaths.MigratePreferences(Old, New);

        Assert.Equal("nuevas", File.ReadAllText(Path.Combine(New, "settings.json")));
    }

    [Fact]
    public void A_missing_previous_folder_is_fine()
    {
        AppPaths.MigratePreferences(Path.Combine(_root, "no-existe"), New);

        Assert.False(File.Exists(Path.Combine(New, "settings.json")));
    }
}
