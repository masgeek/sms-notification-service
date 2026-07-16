using Dapper;
using SmsNotificationService.Models;

namespace SmsNotificationService.Data;

public static class DapperMapper
{
    public static void Register()
    {
        SqlMapper.SetTypeMap(typeof(SmsNotification), new CustomPropertyTypeMap(
            typeof(SmsNotification),
            (type, columnName) => type.GetProperty(
                columnName switch
                {
                    "id" => nameof(SmsNotification.Id),
                    "phone_number" => nameof(SmsNotification.PhoneNumber),
                    "mpesa_code" => nameof(SmsNotification.MpesaCode),
                    "adm_no" => nameof(SmsNotification.AdmNo),
                    "stud_names" => nameof(SmsNotification.StudNames),
                    "amount" => nameof(SmsNotification.Amount),
                    "receipt_no" => nameof(SmsNotification.ReceiptNo),
                    "dated" => nameof(SmsNotification.Dated),
                    "status" => nameof(SmsNotification.Status),
                    "max_retries" => nameof(SmsNotification.MaxRetries),
                    "retry_count" => nameof(SmsNotification.RetryCount),
                    "retry_after" => nameof(SmsNotification.RetryAfter),
                    "created_at" => nameof(SmsNotification.CreatedAt),
                    "updated_at" => nameof(SmsNotification.UpdatedAt),
                    _ => columnName
                })!));
    }
}
