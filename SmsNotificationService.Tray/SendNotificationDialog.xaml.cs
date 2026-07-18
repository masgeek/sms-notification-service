using System.ComponentModel;
using System.Windows;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsNotificationService.Shared;

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

        var configPath = ConfigPathResolver.FindConfigFile();
        var connectionString = ConfigReader.LoadConnectionString(configPath);
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

            var sql = $@"INSERT INTO {Constants.TableName}
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

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
