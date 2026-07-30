using FileProcessing.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using StackExchange.Redis;

namespace FileProcessing.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s,IConfiguration c){s.Configure<StorageOptions>(c.GetSection(StorageOptions.Section));s.Configure<RabbitOptions>(c.GetSection(RabbitOptions.Section));s.Configure<ClamAvOptions>(c.GetSection(ClamAvOptions.Section));s.Configure<OcrOptions>(c.GetSection(OcrOptions.Section));s.Configure<ThumbnailOptions>(c.GetSection(ThumbnailOptions.Section));s.AddDbContext<ProcessingDbContext>(x=>x.UseNpgsql(c.GetConnectionString("PostgreSql")));var minio=c.GetSection(StorageOptions.Section).Get<StorageOptions>()??new();s.AddSingleton<IMinioClient>(_=>new MinioClient().WithEndpoint(minio.Endpoint).WithCredentials(minio.AccessKey,minio.SecretKey).WithSSL(minio.Secure).Build());s.AddSingleton<IConnectionMultiplexer>(_=>ConnectionMultiplexer.Connect(c.GetConnectionString("Redis")??"localhost:6379"));s.AddScoped<IFileRepository,FileRepository>();s.AddScoped<IBlobStorage,MinioBlobStorage>();s.AddSingleton<IJobPublisher,RabbitJobPublisher>();s.AddScoped<IOcrProvider,TesseractOcrProvider>();s.AddScoped<IVirusScanner,ClamAvScanner>();s.AddScoped<IThumbnailGenerator,ImageThumbnailGenerator>();s.AddHttpClient<INotificationSender,WebhookNotificationSender>();return s;}
}
