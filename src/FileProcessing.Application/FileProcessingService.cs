using System.Security.Cryptography;
using FileProcessing.Contracts;
using FileProcessing.Domain;

namespace FileProcessing.Application;

public sealed class FileProcessingOptions
{
    public const string Section = "FileProcessing";
    public long MaxFileSizeBytes { get; init; } = 104_857_600;
    public int MaxRetries { get; init; } = 5;
    public string[] AllowedExtensions { get; init; } = [".pdf", ".docx", ".xlsx", ".txt", ".png", ".jpg", ".jpeg", ".tif", ".tiff"];
    public string[] AllowedContentTypes { get; init; } = ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/plain", "image/png", "image/jpeg", "image/tiff"];
}

public sealed class FileProcessingService(IFileRepository repository, IBlobStorage storage, IJobPublisher publisher, IOcrProvider ocr, IVirusScanner scanner, IThumbnailGenerator thumbnails, IEnumerable<INotificationSender> senders, Microsoft.Extensions.Options.IOptions<FileProcessingOptions> options) : IFileProcessingService
{
    private readonly FileProcessingOptions _options = options.Value;
    public async Task<UploadResponse> UploadAsync(UploadInput input, CancellationToken ct)
    {
        var extension = Path.GetExtension(input.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || !_options.AllowedContentTypes.Contains(input.ContentType, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported file type.");
        if (input.Length <= 0 || input.Length > _options.MaxFileSizeBytes) throw new InvalidDataException("File size is invalid.");
        var tempPath = Path.Combine(Path.GetTempPath(), $"file-processing-{Guid.NewGuid():N}.upload");
        await using var buffered = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        await input.Content.CopyToAsync(buffered, ct); buffered.Position = 0;
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(buffered, ct)).ToLowerInvariant(); buffered.Position = 0;
        if (await repository.FindByChecksumAsync(checksum, ct) is not null) throw new InvalidOperationException("Duplicate file.");
        ValidateSignature(buffered, input.ContentType); buffered.Position = 0;
        var id = Guid.NewGuid(); var objectName = $"original/{id:N}/{Path.GetFileName(input.FileName)}";
        await storage.PutAsync(objectName, buffered, input.ContentType, new Dictionary<string, string> { { "checksum", checksum } }, ct);
        var file = new FileRecord(id, Path.GetFileName(input.FileName), input.ContentType, input.Length, checksum, objectName, DateTimeOffset.UtcNow);
        file.Transition(ProcessingStatus.Validated); file.Transition(ProcessingStatus.Queued);
        await repository.AddAsync(file, ct); await repository.AuditAsync(id, "Upload", input.CorrelationId, checksum, ct); await repository.SaveAsync(ct);
        await publisher.PublishAsync(new FileJob(id, input.CorrelationId), 5, ct);
        return new(id, file.Status.ToString(), checksum);
    }
    public async Task ProcessAsync(FileJob job, CancellationToken ct)
    {
        var file = await repository.GetAsync(job.FileId, ct) ?? throw new InvalidOperationException("File does not exist.");
        if (file.Status == ProcessingStatus.Completed) return;
        try
        {
            file.Transition(ProcessingStatus.VirusScanning); await repository.SaveAsync(ct);
            await using (var stream = await storage.GetAsync(file.ObjectName, ct)) { var scan = await scanner.ScanAsync(stream, ct); await repository.AddScanAsync(new(file.Id, scan.IsInfected, scan.Threat), ct); if (scan.IsInfected) { file.Transition(ProcessingStatus.VirusDetected, scan.Threat); await NotifyAsync(file, "VirusDetected", ct); await repository.SaveAsync(ct); return; } }
            file.Transition(ProcessingStatus.VirusClean); file.Transition(ProcessingStatus.OCRProcessing); await repository.SaveAsync(ct);
            await using (var stream = await storage.GetAsync(file.ObjectName, ct)) { var result = await ocr.ExtractAsync(stream, file.ContentType, ct); await repository.AddOcrAsync(new(file.Id, result.Text, result.Provider), ct); await using var text = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(result.Text)); await storage.PutAsync($"ocr/{file.Id:N}.txt", text, "text/plain", null, ct); }
            file.Transition(ProcessingStatus.OCRCompleted); file.Transition(ProcessingStatus.ThumbnailGenerating); await repository.SaveAsync(ct);
            await using (var stream = await storage.GetAsync(file.ObjectName, ct)) { var thumb = await thumbnails.GenerateAsync(stream, file.ContentType, ct); if (thumb is not null) { var name = $"thumbnails/{file.Id:N}{thumb.Extension}"; await using var content = new MemoryStream(thumb.Content); await storage.PutAsync(name, content, thumb.ContentType, null, ct); await repository.AddThumbnailAsync(new(file.Id, name, thumb.Width, thumb.Height), ct); } }
            file.Transition(ProcessingStatus.ThumbnailCompleted); file.Transition(ProcessingStatus.Stored); await NotifyAsync(file, "ProcessingCompleted", ct); file.Transition(ProcessingStatus.NotificationSent); file.Transition(ProcessingStatus.Completed);
            await repository.AuditAsync(file.Id, "ProcessingCompleted", job.CorrelationId, null, ct); await repository.SaveAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { file.Transition(ProcessingStatus.Failed, ex.Message); await repository.AuditAsync(file.Id, "Failure", job.CorrelationId, ex.Message, ct); await repository.SaveAsync(ct); throw; }
    }
    public async Task RetryAsync(Guid id, string correlationId, CancellationToken ct) { var file = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException(); if (file.RetryCount >= _options.MaxRetries) throw new InvalidOperationException("Maximum retries reached."); file.Retry(); await repository.AuditAsync(id, "Retry", correlationId, null, ct); await repository.SaveAsync(ct); await publisher.PublishAsync(new(id, correlationId, file.RetryCount), 9, ct); }
    private async Task NotifyAsync(FileRecord file, string eventName, CancellationToken ct) { foreach (var sender in senders) { try { await sender.SendAsync(new(file.Id, eventName, file.OriginalName, file.Status.ToString()), ct); await repository.AddNotificationAsync(new(file.Id, sender.Channel, eventName, true, null), ct); } catch (Exception ex) { await repository.AddNotificationAsync(new(file.Id, sender.Channel, eventName, false, ex.Message), ct); } } }
    private static void ValidateSignature(Stream stream, string type) { Span<byte> h = stackalloc byte[8]; var n = stream.Read(h); stream.Position = 0; if (type == "application/pdf" && (n < 4 || !h[..4].SequenceEqual("%PDF"u8))) throw new InvalidDataException("Corrupted PDF."); if (type == "image/png" && (n < 8 || !h.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))) throw new InvalidDataException("Corrupted PNG."); if (type == "image/jpeg" && (n < 2 || h[0] != 0xFF || h[1] != 0xD8)) throw new InvalidDataException("Corrupted JPEG."); if ((type.Contains("officedocument", StringComparison.Ordinal)) && (n < 2 || h[0] != 0x50 || h[1] != 0x4B)) throw new InvalidDataException("Corrupted Office document."); }
}
