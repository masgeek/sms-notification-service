namespace SmsNotificationService.SchoolIntegration;

internal interface IStudentAdapter
{
    string Id { get; }
    string Version { get; }
    IAsyncEnumerable<StudentRecordV1> ReadSnapshotAsync(CancellationToken cancellationToken);
}
