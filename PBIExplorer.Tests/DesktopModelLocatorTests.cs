using PBIExplorer.Services;

namespace PBIExplorer.Tests;

public class DesktopModelLocatorTests
{
    [Fact]
    public void Extracts_the_workspace_data_folder_from_the_engine_command_line()
    {
        const string cmd = "\"C:\\Program Files\\WindowsApps\\PBI\\bin\\msmdsrv.exe\" -c -n AnalysisServicesWorkspace_abc " +
                           "-s \"C:\\Users\\usuario\\Microsoft\\Power BI Desktop Store App\\AnalysisServicesWorkspaces\\AnalysisServicesWorkspace_abc\\Data\"";

        Assert.Equal(
            @"C:\Users\usuario\Microsoft\Power BI Desktop Store App\AnalysisServicesWorkspaces\AnalysisServicesWorkspace_abc\Data",
            DesktopModelLocator.ParseDataDir(cmd));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("msmdsrv.exe -c")]
    public void Returns_null_when_there_is_no_data_folder(string? commandLine) =>
        Assert.Null(DesktopModelLocator.ParseDataDir(commandLine));

    [Theory]
    [InlineData("Ventas 2026 - Power BI Desktop", "Ventas 2026")]
    [InlineData("Ventas 2026* - Power BI Desktop", "Ventas 2026")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Reads_the_report_name_from_the_window_title(string? title, string? expected) =>
        Assert.Equal(expected, PbixLocator.ReportNameFromTitle(title));
}