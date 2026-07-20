using System.Text.RegularExpressions;
using FluentAssertions;

namespace SmsNotificationService.Tests;

/// <summary>
/// Regression tests for the <c>ForceCreate</c> call added to <c>TrayIcon</c>'s constructor
/// in SmsNotificationService.Tray/TrayIcon.cs.
///
/// TrayIcon cannot be exercised as a conventional unit test: it is an <c>internal</c> class
/// living in a WPF/Windows-only project (net10.0-windows) that this test project does not
/// (and, due to target-framework incompatibility, cannot) reference. Its constructor also has
/// no seams for substitution — it directly creates a native <c>H.NotifyIcon.TaskbarIcon</c>
/// (which requires a real Windows shell/message loop), a <c>ServiceMonitor</c> backed by
/// <c>ServiceController</c>, and an <c>UpdateChecker</c> that performs live network calls.
///
/// These tests instead verify, at the source level, that the exact Server-2016 compatibility
/// fix (explicitly disabling Efficiency Mode when the tray icon is force-created) is present
/// and guard against it silently regressing back to the parameterless overload.
/// </summary>
public class TrayIconTests
{
    private const string RelativeSourcePath = "SmsNotificationService.Tray/TrayIcon.cs";

    private static string ReadTrayIconSource()
    {
        var path = FindFileUpwards(AppContext.BaseDirectory, RelativeSourcePath)
            ?? FindFileUpwards(Directory.GetCurrentDirectory(), RelativeSourcePath);

        path.Should().NotBeNull("TrayIcon.cs should be discoverable by walking up from the test output directory");

        return File.ReadAllText(path!);
    }

    private static string? FindFileUpwards(string startDirectory, string relativePath)
    {
        var dir = new DirectoryInfo(startDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }

    [Fact]
    public void Constructor_CallsForceCreate_WithEfficiencyModeExplicitlyDisabled()
    {
        var source = ReadTrayIconSource();

        source.Should().Contain(
            "_icon.ForceCreate(enablesEfficiencyMode: false);",
            "efficiency mode must be explicitly disabled for Windows Server 2016 compatibility");
    }

    [Fact]
    public void Constructor_DoesNotCallParameterlessForceCreate()
    {
        var source = ReadTrayIconSource();

        Regex.IsMatch(source, @"_icon\.ForceCreate\(\s*\)\s*;").Should().BeFalse(
            "the parameterless overload relies on the library default and would re-enable efficiency mode");
    }

    [Fact]
    public void Constructor_CallsForceCreate_ExactlyOnce()
    {
        var source = ReadTrayIconSource();

        var occurrences = Regex.Matches(source, @"\.ForceCreate\(").Count;

        occurrences.Should().Be(1, "ForceCreate should be invoked exactly once during initialization");
    }

    [Fact]
    public void Constructor_CallsForceCreate_AfterIconAndContextMenuAreConfigured()
    {
        var source = ReadTrayIconSource();

        var contextMenuIndex = source.IndexOf("_icon.ContextMenu = BuildContextMenu();", StringComparison.Ordinal);
        var forceCreateIndex = source.IndexOf("_icon.ForceCreate(enablesEfficiencyMode: false);", StringComparison.Ordinal);

        contextMenuIndex.Should().BeGreaterThan(-1);
        forceCreateIndex.Should().BeGreaterThan(contextMenuIndex,
            "the tray icon should be fully configured (icon, tooltip, command, context menu) before it is force-created");
    }

    [Fact]
    public void Constructor_PassesLiteralFalse_NotAVariableOrDefault()
    {
        var source = ReadTrayIconSource();

        // Guards against a future refactor accidentally reintroducing a configurable
        // or defaulted value where an explicit `false` is required for compatibility.
        var match = Regex.Match(source, @"_icon\.ForceCreate\((?<args>[^)]*)\)\s*;");

        match.Success.Should().BeTrue("expected to find the ForceCreate invocation");
        match.Groups["args"].Value.Trim().Should().Be("enablesEfficiencyMode: false");
    }
}