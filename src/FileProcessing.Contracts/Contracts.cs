namespace FileProcessing.Contracts;

public sealed record FileJob(Guid FileId, string CorrelationId, int Attempt = 0);
public sealed record FileResponse(Guid Id, string Name, string ContentType, long Size, string Checksum, string Status, int RetryCount, DateTimeOffset CreatedAt, string? FailureReason);
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record UploadResponse(Guid Id, string Status, string Checksum);
public sealed record NotificationMessage(Guid FileId, string Event, string Name, string Status);
