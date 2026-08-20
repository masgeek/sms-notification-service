using System.IO;
using Microsoft.Win32;
using FeeSyncer.Shared;

namespace FeeSyncer.Tray;

internal static class InstallerFlavorDetector
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string SelfContainedAppId = "{B8E3F2A1-7C4D-4E6F-8A2B-1D3C5E7F9A0B}_is1";
    private const string FrameworkAppId = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}_is1";

    public static UpdateInstallerFlavor Detect()
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll")))
            return UpdateInstallerFlavor.SelfContained;

        var hasSelfContained = HasRegistration(RegistryHive.LocalMachine, SelfContainedAppId) ||
                               HasRegistration(RegistryHive.CurrentUser, SelfContainedAppId);
        var hasFramework = HasRegistration(RegistryHive.LocalMachine, FrameworkAppId) ||
                           HasRegistration(RegistryHive.CurrentUser, FrameworkAppId);
        return hasFramework && !hasSelfContained
            ? UpdateInstallerFlavor.Framework
            : UpdateInstallerFlavor.SelfContained;
    }

    private static bool HasRegistration(RegistryHive hive, string appId)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var uninstall = baseKey.OpenSubKey(UninstallKey);
        using var registration = uninstall?.OpenSubKey(appId);
        return registration is not null;
    }
}
