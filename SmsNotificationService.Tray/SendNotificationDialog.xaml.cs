using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsNotificationService.Tray.Helpers;

namespace SmsNotificationService.Tray;

public partial class SendNotificationDialog : Window
{
    public SendNotificationDialog()
    {
        InitializeComponent();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PhoneBox.Text))
        {
            MessageText.Text = "Phone number is required.";
            MessageText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        if (string.IsNullOrWhiteSpace(MpesaCodeBox.Text))
        {
            MessageText.Text = "M-Pesa code is required.";
            MessageText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        var connectionString = LoadConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            MessageText.Text = "Cannot load connection string from config.";
            MessageText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        try
        {
            SendButton.IsEnabled = false;
            MessageText.Text = "Sending...";
            MessageText.Foreground = System.Windows.Media.Brushes.Gray;

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            var sql = @"INSERT INTO sms_notifications
                (adm_no, stud_names, phone_number, mpesa_code, receipt_no, status, created_at, retry_count)
                VALUES
                (@AdmNo, @StudNames, @PhoneNumber, @MpesaCode, @ReceiptNo, 'PENDING', GETDATE(), 0)";

            var rows = await conn.ExecuteAsync(sql, new
            {
                AdmNo = string.IsNullOrWhiteSpace(AdmNoBox.Text) ? "MANUAL" : AdmNoBox.Text,
                StudNames = StudentNameBox.Text ?? string.Empty,
                PhoneNumber = PhoneBox.Text,
                MpesaCode = MpesaCodeBox.Text,
                ReceiptNo = ReceiptNoBox.Text ?? string.Empty
            });

            if (rows > 0)
            {
                MessageText.Text = "Notification queued successfully.";
                MessageText.Foreground = System.Windows.Media.Brushes.Green;
                ClearForm();
            }
            else
            {
                MessageText.Text = "Failed to insert notification.";
                MessageText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
        catch (Exception ex)
        {
            MessageText.Text = $"Error: {ex.Message}";
            MessageText.Foreground = System.Windows.Media.Brushes.Red;
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ClearForm()
    {
        PhoneBox.Text = string.Empty;
        MpesaCodeBox.Text = string.Empty;
        ReceiptNoBox.Text = string.Empty;
        StudentNameBox.Text = string.Empty;
        AmountBox.Text = string.Empty;
        AdmNoBox.Text = string.Empty;
    }

    private static string LoadConnectionString()
    {
        try
        {
            if (!File.Exists(Paths.ConfigFile)) return string.Empty;
            var json = File.ReadAllText(Paths.ConfigFile);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                cs.TryGetProperty("DefaultConnection", out var conn))
                return conn.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
