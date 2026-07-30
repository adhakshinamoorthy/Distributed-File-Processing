using FileProcessing.Domain;
namespace UnitTests;

public sealed class DomainTests
{
    [Fact] public void Transition_records_traceable_history() { var file = new FileRecord(Guid.NewGuid(), "a.txt", "text/plain", 1, "checksum", "original/a.txt", DateTimeOffset.UtcNow); file.Transition(ProcessingStatus.Validated, "ok"); Assert.Equal(ProcessingStatus.Validated, file.Status); Assert.Contains(file.History, x => x.Status == ProcessingStatus.Validated && x.Detail == "ok"); }
    [Fact] public void Retry_increments_count_and_status() { var file = new FileRecord(Guid.NewGuid(), "a.txt", "text/plain", 1, "checksum", "original/a.txt", DateTimeOffset.UtcNow); file.Retry(); Assert.Equal(1, file.RetryCount); Assert.Equal(ProcessingStatus.Retrying, file.Status); }
    [Fact] public void Delete_soft_deletes_and_cancels() { var file = new FileRecord(Guid.NewGuid(), "a.txt", "text/plain", 1, "checksum", "original/a.txt", DateTimeOffset.UtcNow); file.Delete(); Assert.True(file.IsDeleted); Assert.Equal(ProcessingStatus.Cancelled, file.Status); }
}
