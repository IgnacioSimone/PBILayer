using System.IO;
using PBIExplorer.Infrastructure;

namespace PBIExplorer.Tests;

public class AppSettingsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"pbilayer-{Guid.NewGuid():N}.json");

    [Fact]
    public void Round_trips_theme_window_and_recent_files()
    {
        var path = TempFile();
        try
        {
            var settings = new AppSettings
            {
                Theme = ThemeMode.Dark,
                WindowLeft = 10, WindowTop = 20, WindowWidth = 1200, WindowHeight = 700,
                WindowMaximized = true,
                LastSection = "Tablas"
            };
            settings.AddRecentFile(@"C:\a.pbix");
            settings.Save(path);

            var loaded = AppSettings.Load(path);

            Assert.Equal(ThemeMode.Dark, loaded.Theme);
            Assert.Equal(1200, loaded.WindowWidth);
            Assert.True(loaded.WindowMaximized);
            Assert.Equal("Tablas", loaded.LastSection);
            Assert.Equal(new[] { @"C:\a.pbix" }, loaded.RecentFiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_or_corrupt_file_falls_back_to_defaults()
    {
        var path = TempFile();
        Assert.Equal(ThemeMode.System, AppSettings.Load(path).Theme);

        try
        {
            File.WriteAllText(path, "{ esto no es json");
            var loaded = AppSettings.Load(path);

            Assert.Equal(ThemeMode.System, loaded.Theme);
            Assert.Empty(loaded.RecentFiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Recent_files_are_deduplicated_most_recent_first_and_capped()
    {
        var settings = new AppSettings();
        for (var i = 0; i < 12; i++) settings.AddRecentFile($@"C:\r{i}.pbix");
        settings.AddRecentFile(@"C:\R5.pbix");   // mismo archivo con otra capitalización

        Assert.Equal(AppSettings.MaxRecentFiles, settings.RecentFiles.Count);
        Assert.Equal(@"C:\R5.pbix", settings.RecentFiles[0]);
        Assert.Single(settings.RecentFiles, p => p.Equals(@"C:\r5.pbix", StringComparison.OrdinalIgnoreCase));
    }
}
