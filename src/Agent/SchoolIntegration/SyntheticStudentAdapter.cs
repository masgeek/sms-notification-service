using System.Runtime.CompilerServices;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class SyntheticStudentAdapter : IStudentAdapter
{
    public string Id => "synthetic.students";
    public string Version => "1.0.0";

    public async IAsyncEnumerable<StudentRecordV1> ReadSnapshotAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var number = 1; number <= 250; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new StudentRecordV1
            {
                SourceStudentId = $"synthetic-{number:D5}",
                AdmissionNumber = $"ADM-{number:D5}",
                EnrollmentStatus = "active",
                ClassIdentifier = $"FORM-{((number - 1) % 4) + 1}",
                SourceUpdatedAt = "2026-08-09T00:00:00Z",
            };

            if (number % 50 == 0)
            {
                await Task.Yield();
            }
        }
    }
}
