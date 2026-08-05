using FileProcessing.Contracts;
using FileProcessing.Domain;

namespace FileProcessing.Application;

public sealed record UploadInput(string FileName, string ContentType, long Length, Stream Content, string CorrelationId);
public sealed record OcrOutput(string Text, string Provider);
public sealed record ScanOutput(bool IsInfected, string? Threat);
public sealed record ThumbnailOutput(byte[] Content, string ContentType, int Width, int Height, string Extension);

public interface IFileRepository
{
    Task<FileRecord?> GetAsync(Guid id, CancellationToken ct);
    Task<FileRecord?> FindByChecksumAsync(string checksum, CancellationToken ct);
    Task<(IReadOnlyList<FileRecord> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct);
    Task AddAsync(FileRecord file, CancellationToken ct);
    Task AddOcrAsync(OcrResult result, CancellationToken ct);
    Task AddScanAsync(VirusScanResult result, CancellationToken ct);
    Task AddThumbnailAsync(Thumbnail thumbnail, CancellationToken ct);
    Task AddNotificationAsync(Notification notification, CancellationToken ct);
    Task AuditAsync(Guid? fileId, string action, string correlationId, string? detail, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}
public interface IBlobStorage { Task PutAsync(string objectName, Stream content, string contentType, IReadOnlyDictionary<string, string>? metadata, CancellationToken ct); Task<Stream> GetAsync(string objectName, CancellationToken ct); Task DeleteAsync(string objectName, CancellationToken ct); Task<string> PresignedGetAsync(string objectName, TimeSpan lifetime, CancellationToken ct); }
public interface IJobPublisher { Task PublishAsync(FileJob job, byte priority, CancellationToken ct); }
public interface IOcrProvider { Task<OcrOutput> ExtractAsync(Stream file, string contentType, CancellationToken ct); }
public interface IVirusScanner { Task<ScanOutput> ScanAsync(Stream file, CancellationToken ct); }
public interface IThumbnailGenerator { Task<ThumbnailOutput?> GenerateAsync(Stream file, string contentType, CancellationToken ct); }
public interface INotificationSender { string Channel { get; } Task SendAsync(NotificationMessage message, CancellationToken ct); }
public interface IFileProcessingService
{
    Task<UploadResponse> UploadAsync(UploadInput input, CancellationToken ct);
    Task ProcessAsync(FileJob job, CancellationToken ct);
    Task RetryAsync(Guid id, string correlationId, CancellationToken ct);
}
