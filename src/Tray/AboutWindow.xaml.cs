using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using FeeSyncer.Shared;

namespace FeeSyncer.Tray;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {VersionHelper.GetCurrentVersion()}";
        RuntimeText.Text = $"Runtime: .NET {Environment.Version}";
        CopyrightText.Text = $"Copyright {DateTime.Now.Year} Munywele";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
