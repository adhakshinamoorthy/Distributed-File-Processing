using Microsoft.Extensions.DependencyInjection;
namespace FileProcessing.Application;

public static class DependencyInjection { public static IServiceCollection AddApplication(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration) { services.Configure<FileProcessingOptions>(configuration.GetSection(FileProcessingOptions.Section)); services.AddScoped<IFileProcessingService, FileProcessingService>(); return services; } }
