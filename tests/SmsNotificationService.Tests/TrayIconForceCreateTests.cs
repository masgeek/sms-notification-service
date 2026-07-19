using FluentAssertions;

namespace SmsNotificationService.Tests;

public class TrayIconForceCreateTests
{
    [Fact]
    public void ForceCreate_IsCalledWithEfficiencyModeExplicitlyDisabled()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "SmsNotificationService.Tray", "TrayIcon.cs");
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("_icon.ForceCreate(enablesEfficiencyMode: false);",
            "because efficiency mode must stay disabled so ForceCreate does not invoke SetProcessQualityOfServiceLevel, which is unavailable on Windows Server 2016 / Windows 10 1607 (build 14393) and would crash the tray app on startup there.");
    }
}
