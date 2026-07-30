using System.Text;
using FileProcessing.Api;
using FileProcessing.Application;
using FileProcessing.Contracts;
using FileProcessing.Domain;
using FileProcessing.Infrastructure;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

Log.Logger=new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
try
{
var builder=WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context,services,log)=>log.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services).Enrich.FromLogContext().WriteTo.Console());
builder.Services.AddApplication(builder.Configuration).AddInfrastructure(builder.Configuration);
var max=builder.Configuration.GetValue<long>("FileProcessing:MaxFileSizeBytes",104_857_600);builder.Services.Configure<FormOptions>(x=>x.MultipartBodyLengthLimit=max);
var jwt=builder.Configuration["Authentication:JwtKey"]??throw new InvalidOperationException("Authentication:JwtKey is required.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x=>x.TokenValidationParameters=new(){ValidateIssuer=true,ValidateAudience=true,ValidateLifetime=true,ValidateIssuerSigningKey=true,ValidIssuer=builder.Configuration["Authentication:Issuer"],ValidAudience=builder.Configuration["Authentication:Audience"],IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt))});
builder.Services.AddAuthorizationBuilder().AddPolicy("files",p=>p.RequireRole("admin","processor"));
builder.Services.AddRateLimiter(x=>x.AddFixedWindowLimiter("uploads",o=>{o.PermitLimit=20;o.Window=TimeSpan.FromMinutes(1);o.QueueLimit=10;}));
builder.Services.AddHealthChecks().AddDbContextCheck<ProcessingDbContext>();
builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen();
var connection=builder.Configuration.GetConnectionString("PostgreSql")!;builder.Services.AddHangfire(x=>x.UsePostgreSqlStorage(o=>o.UseNpgsqlConnection(connection)));builder.Services.AddHangfireServer();
builder.Services.AddOpenTelemetry().ConfigureResource(x=>x.AddService("FileProcessing.Api")).WithTracing(x=>x.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter()).WithMetrics(x=>x.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddOtlpExporter());
var app=builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();app.UseSerilogRequestLogging();app.UseRateLimiter();app.UseAuthentication();app.UseMiddleware<ApiKeyMiddleware>();app.UseAuthorization();app.UseSwagger();app.UseSwaggerUI();
app.MapHealthChecks("/health").AllowAnonymous();app.MapHangfireDashboard("/hangfire",new DashboardOptions{Authorization=[new LocalDashboardAuthorization()]});
var files=app.MapGroup("/files").RequireAuthorization("files");
files.MapPost("/upload",async (IFormFile file,HttpContext http,IFileProcessingService service,CancellationToken ct)=>Results.Ok(await service.UploadAsync(new(file.FileName,file.ContentType,file.Length,file.OpenReadStream(),http.TraceIdentifier),ct))).DisableAntiforgery().RequireRateLimiting("uploads");
files.MapPost("/upload/bulk",async (IFormFileCollection uploads,HttpContext http,IFileProcessingService service,CancellationToken ct)=>{var results=new List<UploadResponse>();foreach(var file in uploads)results.Add(await service.UploadAsync(new(file.FileName,file.ContentType,file.Length,file.OpenReadStream(),http.TraceIdentifier),ct));return Results.Ok(results);}).DisableAntiforgery().RequireRateLimiting("uploads");
files.MapGet("/{id:guid}",async (Guid id,IFileRepository repo,CancellationToken ct)=>await repo.GetAsync(id,ct) is { } f?Results.Ok(ToResponse(f)):Results.NotFound());
files.MapGet("/",async (int? page,int? pageSize,IFileRepository repo,CancellationToken ct)=>{var p=Math.Max(1,page??1);var ps=Math.Clamp(pageSize??25,1,100);var (items,total)=await repo.ListAsync(p,ps,ct);return Results.Ok(new PagedResponse<FileResponse>(items.Select(ToResponse).ToList(),p,ps,total));});
files.MapGet("/status/{id:guid}",async (Guid id,IFileRepository repo,CancellationToken ct)=>await repo.GetAsync(id,ct) is { } f?Results.Ok(new{id=f.Id,status=f.Status.ToString(),f.FailureReason,f.RetryCount}):Results.NotFound());
files.MapGet("/download/{id:guid}",async (Guid id,IFileRepository repo,IBlobStorage storage,CancellationToken ct)=>await repo.GetAsync(id,ct) is {Status:ProcessingStatus.Completed} f?Results.Redirect(await storage.PresignedGetAsync(f.ObjectName,TimeSpan.FromMinutes(10),ct)):Results.NotFound());
files.MapPost("/retry/{id:guid}",async (Guid id,HttpContext http,IFileProcessingService service,CancellationToken ct)=>{await service.RetryAsync(id,http.TraceIdentifier,ct);return Results.Accepted($"/files/status/{id}");});
files.MapDelete("/{id:guid}",async (Guid id,HttpContext http,IFileRepository repo,IBlobStorage storage,CancellationToken ct)=>{var file=await repo.GetAsync(id,ct);if(file is null)return Results.NotFound();await storage.DeleteAsync(file.ObjectName,ct);file.Delete();await repo.AuditAsync(id,"Delete",http.TraceIdentifier,null,ct);await repo.SaveAsync(ct);return Results.NoContent();}).RequireAuthorization(p=>p.RequireRole("admin"));
using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();await db.Database.EnsureCreatedAsync();}
RecurringJob.AddOrUpdate<MaintenanceJobs>("retry-failed",x=>x.RetryFailedAsync(CancellationToken.None),Cron.Hourly);RecurringJob.AddOrUpdate<MaintenanceJobs>("cleanup",x=>x.CleanupAsync(CancellationToken.None),Cron.Daily);
await app.RunAsync();
}
catch(Exception ex){Log.Fatal(ex,"Application terminated unexpectedly");}finally{await Log.CloseAndFlushAsync();}
static FileResponse ToResponse(FileRecord f)=>new(f.Id,f.OriginalName,f.ContentType,f.Size,f.Checksum,f.Status.ToString(),f.RetryCount,f.CreatedAt,f.FailureReason);
public partial class Program;
