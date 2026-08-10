namespace SmsNotificationService.SchoolIntegration;

internal sealed class SchoolApiStudentAdapter(SchoolApiClient schoolApi) : IStudentAdapter
{
    public string Id => "school-api";

    public string Version => "1";

    public IAsyncEnumerable<StudentRecordV1> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        return schoolApi.ReadStudentsAsync(250, cancellationToken);
    }
}
