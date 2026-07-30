using FileProcessing.Application;
using FileProcessing.Domain;
using Microsoft.EntityFrameworkCore;

namespace FileProcessing.Infrastructure;

public sealed class ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : DbContext(options)
{
    public DbSet<FileRecord> Files => Set<FileRecord>(); public DbSet<ProcessingHistory> ProcessingHistory => Set<ProcessingHistory>(); public DbSet<AuditLog> AuditLogs => Set<AuditLog>(); public DbSet<OcrResult> OcrResults => Set<OcrResult>(); public DbSet<VirusScanResult> VirusScanResults => Set<VirusScanResult>(); public DbSet<Thumbnail> Thumbnails => Set<Thumbnail>(); public DbSet<Notification> Notifications => Set<Notification>();
    protected override void OnModelCreating(ModelBuilder b) { b.Entity<FileRecord>(e => { e.ToTable("Files"); e.HasKey(x => x.Id); e.HasIndex(x => x.Checksum).IsUnique(); e.Property(x => x.OriginalName).HasMaxLength(512); e.Property(x => x.ContentType).HasMaxLength(200); e.Property(x => x.Checksum).HasMaxLength(64); e.Property(x => x.Status).HasConversion<string>(); e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade); }); b.Entity<ProcessingHistory>(e => { e.ToTable("ProcessingHistory"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>(); }); b.Entity<AuditLog>(e => { e.ToTable("AuditLogs"); e.HasKey(x => x.Id); e.HasIndex(x => x.CorrelationId); }); b.Entity<OcrResult>(e => { e.ToTable("OcrResults"); e.HasKey(x => x.Id); e.HasIndex(x => x.FileId); }); b.Entity<VirusScanResult>(e => { e.ToTable("VirusScanResults"); e.HasKey(x => x.Id); }); b.Entity<Thumbnail>(e => { e.ToTable("Thumbnails"); e.HasKey(x => x.Id); }); b.Entity<Notification>(e => { e.ToTable("Notifications"); e.HasKey(x => x.Id); }); }
}

public sealed class FileRepository(ProcessingDbContext db) : IFileRepository
{
    public Task<FileRecord?> GetAsync(Guid id, CancellationToken ct) => db.Files.Include(x => x.History).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    public Task<FileRecord?> FindByChecksumAsync(string checksum, CancellationToken ct) => db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Checksum == checksum && !x.IsDeleted, ct);
    public async Task<(IReadOnlyList<FileRecord> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct) { var query = db.Files.AsNoTracking().Where(x => !x.IsDeleted); var total = await query.CountAsync(ct); var items = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct); return (items, total); }
    public Task AddAsync(FileRecord file, CancellationToken ct) => db.Files.AddAsync(file, ct).AsTask();
    public Task AddOcrAsync(OcrResult value, CancellationToken ct) => db.OcrResults.AddAsync(value, ct).AsTask(); public Task AddScanAsync(VirusScanResult value, CancellationToken ct) => db.VirusScanResults.AddAsync(value, ct).AsTask(); public Task AddThumbnailAsync(Thumbnail value, CancellationToken ct) => db.Thumbnails.AddAsync(value, ct).AsTask(); public Task AddNotificationAsync(Notification value, CancellationToken ct) => db.Notifications.AddAsync(value, ct).AsTask();
    public Task AuditAsync(Guid? fileId, string action, string correlationId, string? detail, CancellationToken ct) => db.AuditLogs.AddAsync(new(fileId, action, correlationId, detail), ct).AsTask(); public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
