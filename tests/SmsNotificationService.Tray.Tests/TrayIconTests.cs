using System.Reflection;
using FluentAssertions;
using H.NotifyIcon;
using SmsNotificationService.Tray;

namespace SmsNotificationService.Tray.Tests;

/// <summary>
/// Covers the TrayIcon constructor, in particular the switch from the
/// parameterless <c>TaskbarIcon.ForceCreate()</c> call to
/// <c>ForceCreate(enablesEfficiencyMode: false)</c> made to keep tray icon
/// creation working on Windows Server 2016.
///
/// TaskbarIcon (H.NotifyIcon) is a WPF component, so every test that touches
/// it must run on a dedicated STA thread with its own Dispatcher, which is
/// what <see cref="RunOnSta"/> provides.
/// </summary>
public class TrayIconTests
{
    private static void RunOnSta(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    private static TaskbarIcon GetUnderlyingTaskbarIcon(TrayIcon trayIcon)
    {
        var field = typeof(TrayIcon).GetField("_icon", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("TrayIcon is expected to keep a private '_icon' field of type TaskbarIcon");
        return (TaskbarIcon)field!.GetValue(trayIcon)!;
    }

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        RunOnSta(() =>
        {
            TrayIcon? trayIcon = null;

            try
            {
                var act = () => trayIcon = new TrayIcon();
                act.Should().NotThrow();
            }
            finally
            {
                trayIcon?.Dispose();
            }
        });
    }

    [Fact]
    public void Constructor_ForceCreatesTaskbarIcon_WithEfficiencyModeDisabled()
    {
        RunOnSta(() =>
        {
            var trayIcon = new TrayIcon();

            try
            {
                var icon = GetUnderlyingTaskbarIcon(trayIcon);

                icon.Should().NotBeNull();
                icon.IsCreated.Should().BeTrue(
                    "ForceCreate(enablesEfficiencyMode: false) is expected to create the underlying " +
                    "taskbar icon just like the previous parameterless ForceCreate() call did");
            }
            finally
            {
                trayIcon.Dispose();
            }
        });
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        RunOnSta(() =>
        {
            var trayIcon = new TrayIcon();

            var act = () => trayIcon.Dispose();

            act.Should().NotThrow();
        });
    }

    [Fact]
    public void Constructor_CreatingMultipleInstancesSequentially_DoesNotThrow()
    {
        // Regression guard for the Server 2016 compatibility fix: creating
        // and disposing several TrayIcon instances in sequence must keep
        // working now that ForceCreate is called with enablesEfficiencyMode: false.
        RunOnSta(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                var act = () =>
                {
                    var trayIcon = new TrayIcon();
                    trayIcon.Dispose();
                };

                act.Should().NotThrow();
            }
        });
    }
}