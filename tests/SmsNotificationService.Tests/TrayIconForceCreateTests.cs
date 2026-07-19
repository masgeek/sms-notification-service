using System.Runtime.CompilerServices;
using FluentAssertions;

namespace SmsNotificationService.Tests;

/// <summary>
/// TrayIcon lives in the Windows/WPF-only SmsNotificationService.Tray project
/// (net10.0-windows, UseWPF) and is not referenced by this cross-platform test
/// project. Its constructor also has no seams for dependency injection: it
/// directly instantiates a real H.NotifyIcon TaskbarIcon and immediately calls
/// ForceCreate(), which talks to the live Windows shell notification area.
/// That makes the class impossible to exercise in a conventional unit test
/// without a production refactor.
///
/// The change under test is a one-line, security/compat-critical fix:
/// ForceCreate() -> ForceCreate(enablesEfficiencyMode: false). Passing
/// enablesEfficiencyMode: true (the library default) calls
/// SetProcessQualityOfServiceLevel, which is unavailable on Windows Server
/// 2016 / Windows 10 1607 (build 14393) and throws, crashing the tray app on
/// startup on those OS versions.
///
/// These tests act as a regression guard against that exact call being
/// reverted or altered, without requiring a Windows/WPF runtime.
/// </summary>
public class TrayIconForceCreateTests
{
    private const string ExpectedForceCreateCall = "_icon.ForceCreate(enablesEfficiencyMode: false);";

    private static string GetTrayIconSourcePath([CallerFilePath] string testFilePath = "")
    {
        var testsDir = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "SmsNotificationService.Tray", "TrayIcon.cs"));
    }

    private static string ReadTrayIconSource()
    {
        var path = GetTrayIconSourcePath();
        File.Exists(path).Should().BeTrue($"the TrayIcon.cs source should exist at '{path}'");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ForceCreate_IsCalledWithEfficiencyModeExplicitlyDisabled()
    {
        var source = ReadTrayIconSource();

        source.Should().Contain(ExpectedForceCreateCall,
            "efficiency mode must stay disabled so ForceCreate does not invoke " +
            "SetProcessQualityOfServiceLevel, which is unavailable on Windows Server 2016 / " +
            "Windows 10 1607 (build 14393) and would crash the tray app on startup there");
    }

    [Fact]
    public void ForceCreate_IsNotCalledWithNoArguments()
    {
        var source = ReadTrayIconSource();

        source.Should().NotContain("_icon.ForceCreate();",
            "calling the parameterless overload re-enables efficiency mode by default " +
            "and reintroduces the Server 2016 compatibility crash");
    }

    [Fact]
    public void ForceCreate_IsNotCalledWithEfficiencyModeEnabled()
    {
        var source = ReadTrayIconSource();

        source.Should().NotContain("enablesEfficiencyMode: true",
            "explicitly enabling efficiency mode would reintroduce the Server 2016 compatibility crash");
    }

    [Fact]
    public void ForceCreate_IsCalledExactlyOnce()
    {
        var source = ReadTrayIconSource();

        var occurrences = source.Split("ForceCreate(").Length - 1;

        occurrences.Should().Be(1, "TrayIcon should create the taskbar icon exactly once during construction");
    }
}