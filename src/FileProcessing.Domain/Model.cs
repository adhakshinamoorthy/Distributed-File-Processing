namespace FileProcessing.Domain;

public enum ProcessingStatus { Uploaded, Validated, Queued, OCRProcessing, OCRCompleted, VirusScanning, VirusClean, VirusDetected, ThumbnailGenerating, ThumbnailCompleted, Stored, NotificationSent, Completed, Failed, Cancelled, Retrying }
public enum FileKind { Original, Processed, Thumbnail, OcrOutput }

public sealed class FileRecord
{
    private FileRecord() { }
    public FileRecord(Guid id, string name, string contentType, long size, string checksum, string objectName, DateTimeOffset now)
    { Id = id; OriginalName = name; ContentType = contentType; Size = size; Checksum = checksum; ObjectName = objectName; Status = ProcessingStatus.Uploaded; CreatedAt = UpdatedAt = now; }
    public Guid Id { get; private set; }
    public string OriginalName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long Size { get; private set; }
    public string Checksum { get; private set; } = "";
    public string ObjectName { get; private set; } = "";
    public ProcessingStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public ICollection<ProcessingHistory> History { get; private set; } = [];
    public void Transition(ProcessingStatus status, string? detail = null) { Status = status; FailureReason = status == ProcessingStatus.Failed ? detail : null; UpdatedAt = DateTimeOffset.UtcNow; History.Add(new(Id, status, detail)); }
    public void Retry() { RetryCount++; Transition(ProcessingStatus.Retrying); }
    public void Delete() { IsDeleted = true; Transition(ProcessingStatus.Cancelled, "Deleted"); }
}
public sealed class ProcessingHistory { private ProcessingHistory() { } public ProcessingHistory(Guid fileId, ProcessingStatus status, string? detail) { Id = Guid.NewGuid(); FileId = fileId; Status = status; Detail = detail; OccurredAt = DateTimeOffset.UtcNow; } public Guid Id { get; private set; } public Guid FileId { get; private set; } public ProcessingStatus Status { get; private set; } public string? Detail { get; private set; } public DateTimeOffset OccurredAt { get; private set; } }
public sealed class AuditLog { private AuditLog() { } public AuditLog(Guid? fileId, string action, string correlationId, string? detail) { Id = Guid.NewGuid(); FileId = fileId; Action = action; CorrelationId = correlationId; Detail = detail; OccurredAt = DateTimeOffset.UtcNow; } public Guid Id { get; private set; } public Guid? FileId { get; private set; } public string Action { get; private set; } = ""; public string CorrelationId { get; private set; } = ""; public string? Detail { get; private set; } public DateTimeOffset OccurredAt { get; private set; } }
public sealed class OcrResult { private OcrResult() { } public OcrResult(Guid fileId, string text, string provider) { Id = Guid.NewGuid(); FileId = fileId; Text = text; Provider = provider; CreatedAt = DateTimeOffset.UtcNow; } public Guid Id { get; private set; } public Guid FileId { get; private set; } public string Text { get; private set; } = ""; public string Provider { get; private set; } = ""; public DateTimeOffset CreatedAt { get; private set; } }
public sealed class VirusScanResult { private VirusScanResult() { } public VirusScanResult(Guid fileId, bool infected, string? threat) { Id = Guid.NewGuid(); FileId = fileId; IsInfected = infected; Threat = threat; ScannedAt = DateTimeOffset.UtcNow; } public Guid Id { get; private set; } public Guid FileId { get; private set; } public bool IsInfected { get; private set; } public string? Threat { get; private set; } public DateTimeOffset ScannedAt { get; private set; } }
public sealed class Thumbnail { private Thumbnail() { } public Thumbnail(Guid fileId, string objectName, int width, int height) { Id = Guid.NewGuid(); FileId = fileId; ObjectName = objectName; Width = width; Height = height; CreatedAt = DateTimeOffset.UtcNow; } public Guid Id { get; private set; } public Guid FileId { get; private set; } public string ObjectName { get; private set; } = ""; public int Width { get; private set; } public int Height { get; private set; } public DateTimeOffset CreatedAt { get; private set; } }
public sealed class Notification { private Notification() { } public Notification(Guid fileId, string channel, string eventName, bool delivered, string? error) { Id = Guid.NewGuid(); FileId = fileId; Channel = channel; EventName = eventName; Delivered = delivered; Error = error; CreatedAt = DateTimeOffset.UtcNow; } public Guid Id { get; private set; } public Guid FileId { get; private set; } public string Channel { get; private set; } = ""; public string EventName { get; private set; } = ""; public bool Delivered { get; private set; } public string? Error { get; private set; } public DateTimeOffset CreatedAt { get; private set; } }
