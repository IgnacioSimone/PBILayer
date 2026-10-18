using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PBIExplorer.Infrastructure;
using PBIExplorer.Services.Updates;
using PBIExplorer.ViewModels;

namespace PBIExplorer.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pbicontext-update-" + Guid.NewGuid().ToString("N"));

    public UpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* lo limpia el sistema después */ }
    }

    // ---------------- Infraestructura de prueba ----------------

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> _routes = new();
        public List<string> Requests { get; } = new();
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHandler Serve(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            Serve(url, Encoding.UTF8.GetBytes(body), status);

        public FakeHandler Serve(string url, byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[url] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request.RequestUri!.ToString());

            if (!_routes.TryGetValue(request.RequestUri.ToString(), out var route))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(route.Status) { Content = new ByteArrayContent(route.Body) });
        }
    }

    private sealed class FakeFeed : IUpdateFeed
    {
        public ReleaseInfo? Latest { get; set; }
        public Exception? Error { get; set; }
        public int Calls { get; private set; }

        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
        {
            Calls++;
            return Error is null ? Task.FromResult(Latest) : Task.FromException<ReleaseInfo?>(Error);
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<string> Installers { get; } = new();
        public List<string> Opened { get; } = new();
        public void ShowInfo(string message) { }
        public void ShowWarning(string message, string? title = null) { }
        public string? PickOpenFile(string filter) => null;
        public string? PickSaveFile(string filter, string suggestedName) => null;
        public void CopyText(string text) { }
        public void RevealInExplorer(string path) { }
        public void RunInstallerAndExit(string path) => Installers.Add(path);
        public void RestartApplication() { }
        public void OpenWithShell(string path) => Opened.Add(path);
    }

    private const string Url = "https://github.com/IgnacioSimone/PBIContext/releases/download/v2.1.0/PBIContext-Setup-2.1.0.exe";
    private const string ShaUrl = Url + ".sha256";

    private static ReleaseInfo Release(string version = "2.1.0", bool installable = true) => new(
        Version.Parse(version), "v" + version, "Notas de la versión", "https://github.com/IgnacioSimone/PBIContext/releases/tag/v" + version,
        installable ? $"PBIContext-Setup-{version}.exe" : null, installable ? Url : null, installable ? ShaUrl : null);

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // ---------------- Lectura de la release de GitHub ----------------

    private const string ReleaseJson = """
        {
          "tag_name": "v2.1.0", "draft": false, "prerelease": false,
          "html_url": "https://github.com/IgnacioSimone/PBIContext/releases/tag/v2.1.0",
          "body": "- Novedad A\n- Novedad B",
          "assets": [
            { "name": "PBIContext-Setup-2.1.0.exe", "browser_download_url": "https://github.com/IgnacioSimone/PBIContext/releases/download/v2.1.0/PBIContext-Setup-2.1.0.exe" },
            { "name": "PBIContext-Setup-2.1.0.exe.sha256", "browser_download_url": "https://github.com/IgnacioSimone/PBIContext/releases/download/v2.1.0/PBIContext-Setup-2.1.0.exe.sha256" },
            { "name": "otra-cosa.zip", "browser_download_url": "https://example.com/otra-cosa.zip" }
          ]
        }
        """;

    [Fact]
    public void Parses_version_notes_installer_and_checksum()
    {
        var release = GitHubUpdateFeed.Parse(ReleaseJson)!;

        Assert.Equal(new Version(2, 1, 0), release.Version);
        Assert.Equal("v2.1.0", release.Tag);
        Assert.Contains("Novedad B", release.Notes);
        Assert.Equal("PBIContext-Setup-2.1.0.exe", release.InstallerName);
        Assert.Equal(Url, release.InstallerUrl);
        Assert.Equal(ShaUrl, release.ChecksumUrl);
        Assert.True(release.CanInstall);
    }

    [Fact]
    public void A_release_without_checksum_cannot_be_installed_automatically()
    {
        var json = ReleaseJson.Replace("PBIContext-Setup-2.1.0.exe.sha256", "algo.txt");

        var release = GitHubUpdateFeed.Parse(json)!;

        Assert.NotNull(release.InstallerUrl);
        Assert.Null(release.ChecksumUrl);
        Assert.False(release.CanInstall);
    }

    [Theory]
    [InlineData("""{"tag_name":"v9.9.9","draft":true,"assets":[]}""")]
    [InlineData("""{"tag_name":"v9.9.9","prerelease":true,"assets":[]}""")]
    [InlineData("""{"tag_name":"nightly","assets":[]}""")]
    [InlineData("""{"assets":[]}""")]
    public void Drafts_prereleases_and_non_version_tags_are_ignored(string json) =>
        Assert.Null(GitHubUpdateFeed.Parse(json));

    [Theory]
    [InlineData("v2.1.0", "2.1.0")]
    [InlineData("2.1", "2.1.0")]
    [InlineData("V3.0.4-beta.1", "3.0.4")]
    [InlineData("v1.2.3+abc", "1.2.3")]
    public void Tags_become_three_part_versions(string tag, string expected)
    {
        Assert.True(GitHubUpdateFeed.TryParseTag(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    // ---------------- Consulta a GitHub ----------------

    private static GitHubUpdateFeed FeedFor(FakeHandler handler) =>
        new(new HttpClient(handler), "IgnacioSimone", "PBIContext");

    private const string ApiUrl = "https://api.github.com/repos/IgnacioSimone/PBIContext/releases/latest";

    [Fact]
    public async Task The_feed_queries_the_public_releases_api_and_sends_nothing_else()
    {
        var handler = new FakeHandler().Serve(ApiUrl, ReleaseJson);

        var release = await FeedFor(handler).GetLatestAsync(CancellationToken.None);

        Assert.Equal(new Version(2, 1, 0), release!.Version);
        Assert.Equal(new[] { ApiUrl }, handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Null(handler.LastRequest.Content);
        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Missing_or_private_repositories_mean_no_releases(HttpStatusCode status)
    {
        var handler = new FakeHandler().Serve(ApiUrl, "{}", status);

        Assert.Null(await FeedFor(handler).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Server_errors_are_reported_as_failures()
    {
        var handler = new FakeHandler().Serve(ApiUrl, "{}", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(() => FeedFor(handler).GetLatestAsync(CancellationToken.None));
    }

    // ---------------- Comparación de versiones ----------------

    [Theory]
    [InlineData("2.1.0", "2.0.0", UpdateStatus.Available)]
    [InlineData("2.0.1", "2.0.0", UpdateStatus.Available)]
    [InlineData("3.0.0", "2.9.9", UpdateStatus.Available)]
    [InlineData("2.0.0", "2.0.0", UpdateStatus.UpToDate)]
    [InlineData("1.9.0", "2.0.0", UpdateStatus.UpToDate)]
    public async Task The_checker_compares_published_and_installed_versions(string published, string installed, UpdateStatus expected)
    {
        var result = await UpdateChecker.CheckAsync(new FakeFeed { Latest = Release(published) }, Version.Parse(installed));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task The_checker_reports_no_releases_and_failures_without_throwing()
    {
        Assert.Equal(UpdateStatus.NoReleases, (await UpdateChecker.CheckAsync(new FakeFeed(), new Version(2, 0, 0))).Status);

        var failed = await UpdateChecker.CheckAsync(new FakeFeed { Error = new HttpRequestException("sin internet") }, new Version(2, 0, 0));
        Assert.Equal(UpdateStatus.Failed, failed.Status);
        Assert.Contains("sin internet", failed.Error);
    }

    // ---------------- Descarga y verificación ----------------

    private UpdateInstaller InstallerFor(FakeHandler handler) => new(new HttpClient(handler), _dir);

    [Fact]
    public async Task A_download_with_a_matching_hash_is_saved_and_returned()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 5000));
        var handler = new FakeHandler().Serve(Url, bytes).Serve(ShaUrl, $"{Sha(bytes)}  PBIContext-Setup-2.1.0.exe\n");
        var progress = new List<double>();

        var path = await InstallerFor(handler).DownloadAndVerifyAsync(Release(), new Progress<double>(progress.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "PBIContext-Setup-2.1.0.exe"), path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task A_corrupted_download_is_rejected_and_nothing_is_left_behind()
    {
        var good = Encoding.UTF8.GetBytes("instalador bueno");
        var handler = new FakeHandler().Serve(Url, "instalador MALO").Serve(ShaUrl, Sha(good));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            InstallerFor(handler).DownloadAndVerifyAsync(Release(), null, CancellationToken.None));

        Assert.Contains("no coincide", error.Message);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Without_a_checksum_nothing_is_downloaded()
    {
        var handler = new FakeHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InstallerFor(handler).DownloadAndVerifyAsync(Release(installable: false), null, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://github.com/x/PBIContext-Setup-2.1.0.exe")]
    [InlineData("https://evil.example.com/PBIContext-Setup-2.1.0.exe")]
    [InlineData("https://github.com.evil.example/PBIContext-Setup-2.1.0.exe")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    public async Task Only_https_downloads_from_github_are_allowed(string badUrl)
    {
        var handler = new FakeHandler();
        var release = Release() with { InstallerUrl = badUrl };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InstallerFor(handler).DownloadAndVerifyAsync(release, null, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_installer_name_from_the_network_cannot_escape_the_updates_folder()
    {
        var bytes = Encoding.UTF8.GetBytes("contenido");
        var handler = new FakeHandler().Serve(Url, bytes).Serve(ShaUrl, Sha(bytes));
        var release = Release() with { InstallerName = @"..\..\evil.exe" };

        var path = await InstallerFor(handler).DownloadAndVerifyAsync(release, null, CancellationToken.None);

        Assert.Equal(_dir, Path.GetDirectoryName(path));
        Assert.Equal("evil.exe", Path.GetFileName(path));
    }

    [Theory]
    [InlineData("https://github.com/a/b", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("HTTPS://GITHUB.COM/a", true)]
    [InlineData("http://github.com/a", false)]
    [InlineData("https://notgithub.com/a", false)]
    [InlineData("no es una url", false)]
    public void Trusted_url_rules(string url, bool expected) => Assert.Equal(expected, UpdateInstaller.IsTrustedUrl(url));

    [Fact]
    public void Checksum_files_in_either_format_are_understood()
    {
        var hash = new string('a', 64);

        Assert.Equal(hash, UpdateInstaller.ParseChecksum(hash + "\n", "x.exe"));
        Assert.Equal(hash, UpdateInstaller.ParseChecksum($"{hash.ToUpperInvariant()}  x.exe", "x.exe"));
        Assert.Equal(hash, UpdateInstaller.ParseChecksum($"{hash} *x.exe", "x.exe"));
        Assert.Null(UpdateInstaller.ParseChecksum($"{hash}  otro.exe", "x.exe"));
        Assert.Null(UpdateInstaller.ParseChecksum("no es un hash", "x.exe"));
        Assert.Null(UpdateInstaller.ParseChecksum(new string('z', 64), "x.exe"));
    }

    // ---------------- ViewModel ----------------

    private (UpdateViewModel Vm, FakeFeed Feed, AppSettings Settings, FakeDialogs Dialogs, int[] Saves) CreateVm(
        ReleaseInfo? latest = null, Func<DateTime>? clock = null, UpdateCheckMode mode = UpdateCheckMode.Unset, HttpMessageHandler? http = null)
    {
        var feed = new FakeFeed { Latest = latest };
        var settings = new AppSettings { UpdateChecks = mode };
        var dialogs = new FakeDialogs();
        var saves = new[] { 0 };
        var installer = new UpdateInstaller(new HttpClient(http ?? new FakeHandler()), _dir);
        var vm = new UpdateViewModel(feed, installer, dialogs, settings, () => saves[0]++, new Version(2, 0, 0), clock);
        return (vm, feed, settings, dialogs, saves);
    }

    [Fact]
    public async Task By_default_nothing_is_checked_automatically()
    {
        var (vm, feed, _, _, _) = CreateVm(Release());

        Assert.Null(await vm.AutoCheckIfDueAsync());
        Assert.Equal(0, feed.Calls);
        Assert.False(vm.AutoCheck);
    }

    [Fact]
    public async Task With_automatic_checks_on_it_queries_once_per_interval()
    {
        var now = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
        var (vm, feed, settings, _, _) = CreateVm(Release(), () => now, UpdateCheckMode.On);

        var first = await vm.AutoCheckIfDueAsync();
        Assert.Equal(UpdateStatus.Available, first!.Status);
        Assert.Equal(now, settings.LastUpdateCheckUtc);

        now = now.AddHours(3);
        Assert.Null(await vm.AutoCheckIfDueAsync());   // todavía no pasó el intervalo
        Assert.Equal(1, feed.Calls);

        now = now.AddHours(21);
        Assert.NotNull(await vm.AutoCheckIfDueAsync());
        Assert.Equal(2, feed.Calls);
    }

    [Fact]
    public async Task A_failed_check_does_not_count_as_checked_today()
    {
        var (vm, feed, settings, _, _) = CreateVm(mode: UpdateCheckMode.On);
        feed.Error = new HttpRequestException("sin internet");

        var result = await vm.AutoCheckIfDueAsync();

        Assert.Equal(UpdateStatus.Failed, result!.Status);
        Assert.Null(settings.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task A_manual_check_shows_the_result_and_the_notes()
    {
        var (vm, _, _, _, _) = CreateVm(Release("2.1.0"));

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.True(vm.IsUpdateAvailable);
        Assert.True(vm.CanInstall);
        Assert.Equal("Notas de la versión", vm.Notes);
        Assert.Contains("2.1.0", vm.StatusText);
    }

    [Fact]
    public async Task A_manual_check_explains_when_there_is_nothing_published()
    {
        var (vm, _, _, _, _) = CreateVm();

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.False(vm.IsUpdateAvailable);
        Assert.Contains("repositorio puede ser privado", vm.StatusText);
    }

    [Fact]
    public async Task A_manual_check_reports_network_problems()
    {
        var (vm, feed, _, _, _) = CreateVm();
        feed.Error = new HttpRequestException("sin internet");

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.Contains("No se pudo buscar", vm.StatusText);
        Assert.Contains("sin internet", vm.StatusText);
    }

    [Fact]
    public void Turning_automatic_checks_on_or_off_is_saved_and_the_prompt_is_not_repeated()
    {
        var (vm, _, settings, _, saves) = CreateVm();

        vm.AutoCheck = true;
        Assert.Equal(UpdateCheckMode.On, settings.UpdateChecks);
        Assert.True(settings.UpdatePromptShown);

        vm.AutoCheck = false;
        Assert.Equal(UpdateCheckMode.Off, settings.UpdateChecks);
        Assert.Equal(2, saves[0]);
    }

    [Fact]
    public async Task Installing_downloads_verifies_and_hands_off_to_the_installer()
    {
        var bytes = Encoding.UTF8.GetBytes("instalador");
        var handler = new FakeHandler().Serve(Url, bytes).Serve(ShaUrl, Sha(bytes));
        var (vm, _, _, dialogs, _) = CreateVm(Release(), http: handler);
        await vm.CheckNowCommand.ExecuteAsync(null);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(new[] { Path.Combine(_dir, "PBIContext-Setup-2.1.0.exe") }, dialogs.Installers);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task A_bad_download_is_reported_and_the_installer_never_runs()
    {
        var handler = new FakeHandler().Serve(Url, "MALO").Serve(ShaUrl, Sha(Encoding.UTF8.GetBytes("bueno")));
        var (vm, _, _, dialogs, _) = CreateVm(Release(), http: handler);
        await vm.CheckNowCommand.ExecuteAsync(null);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.Installers);
        Assert.Contains("No se pudo actualizar", vm.StatusText);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task Installing_does_nothing_when_there_is_no_update()
    {
        var (vm, _, _, dialogs, _) = CreateVm(Release("2.0.0"));
        await vm.CheckNowCommand.ExecuteAsync(null);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.Installers);
    }

    [Fact]
    public async Task The_release_page_opens_only_if_it_is_on_github()
    {
        var (vm, _, _, dialogs, _) = CreateVm(Release());
        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.OpenReleasePageCommand.Execute(null);

        Assert.Equal(new[] { Release().PageUrl }, dialogs.Opened);
    }

    // ---------------- Integración con la ventana principal ----------------

    private static MainViewModel CreateMain(UpdateViewModel updates, AppSettings settings) =>
        new(new StubBackend(), new FakeDialogs(), settings, null, updates) { ToastDuration = Timeout.InfiniteTimeSpan };

    private sealed class StubBackend : PBIExplorer.Services.IPowerBiBackend
    {
        public Task<PBIExplorer.Models.ModelSnapshot> ReadModelAsync(string server, string database) =>
            Task.FromResult(new PBIExplorer.Models.ModelSnapshot());

        public string? GetOpenReportName() => null;
        public Task<string?> FindOpenPbixAsync(TimeSpan budget) => Task.FromResult<string?>(null);
        public Task<PBIExplorer.Services.DesktopModel?> FindDesktopModelAsync(string? reportName) => Task.FromResult<PBIExplorer.Services.DesktopModel?>(null);

        public (List<PBIExplorer.Models.VisualFieldUsage>, List<PBIExplorer.Models.FilterInfo>) ParseReport(string path) => (new(), new());
    }

    [Fact]
    public async Task The_first_launch_asks_once_whether_to_check_for_updates_and_connects_to_nothing()
    {
        var (updates, feed, settings, _, _) = CreateVm(Release());
        var main = CreateMain(updates, settings);

        await main.InitializeAsync(new[] { "PBIContext.exe" });

        var toast = Assert.Single(main.Toasts, t => t.ActionText == "Activar");
        Assert.Contains("no envía ningún dato", toast.Message);
        Assert.Equal(0, feed.Calls);
        Assert.True(settings.UpdatePromptShown);

        // Segundo arranque: no se vuelve a preguntar.
        var again = CreateMain(updates, settings);
        await again.InitializeAsync(new[] { "PBIContext.exe" });
        Assert.DoesNotContain(again.Toasts, t => t.ActionText == "Activar");
    }

    [Fact]
    public async Task Accepting_the_prompt_turns_checks_on_and_opens_about_checking()
    {
        var (updates, _, settings, _, _) = CreateVm(Release());
        var main = CreateMain(updates, settings);
        var requested = new List<bool>();
        main.AboutRequested += requested.Add;
        await main.InitializeAsync(new[] { "PBIContext.exe" });

        main.Toasts.Single(t => t.ActionText == "Activar").RunActionCommand.Execute(null);

        Assert.Equal(UpdateCheckMode.On, settings.UpdateChecks);
        Assert.Equal(new[] { true }, requested);
    }

    [Fact]
    public async Task With_checks_on_a_newer_version_raises_a_toast_that_opens_about()
    {
        var (updates, _, settings, _, _) = CreateVm(Release("2.5.0"), mode: UpdateCheckMode.On);
        settings.UpdatePromptShown = true;
        var main = CreateMain(updates, settings);
        var requested = new List<bool>();
        main.AboutRequested += requested.Add;

        await main.InitializeAsync(new[] { "PBIContext.exe" });

        var toast = Assert.Single(main.Toasts);
        Assert.Contains("2.5.0", toast.Message);
        Assert.Equal("Ver", toast.ActionText);

        toast.RunActionCommand.Execute(null);
        Assert.Equal(new[] { false }, requested);
    }

    [Fact]
    public async Task With_checks_off_the_app_never_asks_or_connects()
    {
        var (updates, feed, settings, _, _) = CreateVm(Release("2.5.0"), mode: UpdateCheckMode.Off);
        var main = CreateMain(updates, settings);

        await main.InitializeAsync(new[] { "PBIContext.exe" });

        Assert.Empty(main.Toasts);
        Assert.Equal(0, feed.Calls);
    }

    // ---------------- Datos de la aplicación ----------------

    [Fact]
    public void The_version_comes_from_the_assembly()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.VersionText);
        Assert.Equal(AppInfo.VersionText, new Version(AppInfo.Version.Major, AppInfo.Version.Minor, AppInfo.Version.Build).ToString());
        Assert.True(AppInfo.Version >= new Version(2, 0, 0));
    }

    [Fact]
    public void Settings_remember_the_update_choice()
    {
        var path = Path.Combine(_dir, "settings.json");
        var settings = new AppSettings
        {
            UpdateChecks = UpdateCheckMode.On,
            UpdatePromptShown = true,
            LastUpdateCheckUtc = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc)
        };
        settings.Save(path);

        var loaded = AppSettings.Load(path);

        Assert.Equal(UpdateCheckMode.On, loaded.UpdateChecks);
        Assert.True(loaded.UpdatePromptShown);
        Assert.Equal(settings.LastUpdateCheckUtc, loaded.LastUpdateCheckUtc);
        Assert.Equal(UpdateCheckMode.Unset, new AppSettings().UpdateChecks);
    }

    [Theory]
    [InlineData("https://github.com/IgnacioSimone/PBIContext", true)]
    [InlineData("https://github.com/IgnacioSimone/PBIContext/blob/master/LICENSE", true)]
    [InlineData("https://github.com/otra-persona/PBIContext", false)]
    [InlineData("https://evil.example.com", false)]
    [InlineData("", false)]
    public void About_only_opens_links_to_the_project(string url, bool opens)
    {
        var dialogs = new FakeDialogs();
        var (updates, _, _, _, _) = CreateVm();
        var about = new AboutViewModel(updates, dialogs);

        about.OpenUrlCommand.Execute(url);

        Assert.Equal(opens ? 1 : 0, dialogs.Opened.Count);
    }
}
