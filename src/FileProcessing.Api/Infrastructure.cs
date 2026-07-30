using Hangfire.Dashboard;
using Microsoft.EntityFrameworkCore;
using FileProcessing.Application;
using FileProcessing.Domain;
using FileProcessing.Infrastructure;
using System.Security.Cryptography;

namespace FileProcessing.Api;
public sealed class CorrelationIdMiddleware(RequestDelegate next){public async Task InvokeAsync(HttpContext c){var id=c.Request.Headers.TryGetValue("X-Correlation-ID",out var supplied)&&Guid.TryParse(supplied,out _)?supplied.ToString():Guid.NewGuid().ToString();c.TraceIdentifier=id;c.Response.Headers["X-Correlation-ID"]=id;await next(c);}}
public sealed class ApiKeyMiddleware(RequestDelegate next,IConfiguration configuration){public async Task InvokeAsync(HttpContext c){if(c.User.Identity?.IsAuthenticated!=true&&c.Request.Headers.TryGetValue("X-Api-Key",out var supplied)&&CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied.ToString()),System.Text.Encoding.UTF8.GetBytes(configuration["Authentication:ApiKey"]??"disabled"))){var claims=new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name,"api-key"),new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"processor")};c.User=new(new System.Security.Claims.ClaimsIdentity(claims,"ApiKey"));}await next(c);}}
public sealed class LocalDashboardAuthorization:IDashboardAuthorizationFilter { public bool Authorize(DashboardContext context){var http=context.GetHttpContext();return http.Connection.RemoteIpAddress is { } ip&&System.Net.IPAddress.IsLoopback(ip);} }
public sealed class MaintenanceJobs(ProcessingDbContext db,IJobPublisher publisher){public async Task RetryFailedAsync(CancellationToken ct){var files=await db.Files.Where(x=>x.Status==ProcessingStatus.Failed&&x.RetryCount<5).Take(100).ToListAsync(ct);foreach(var file in files){file.Retry();await publisher.PublishAsync(new(file.Id,Guid.NewGuid().ToString(),file.RetryCount),9,ct);}await db.SaveChangesAsync(ct);}public async Task CleanupAsync(CancellationToken ct){var cutoff=DateTimeOffset.UtcNow.AddDays(-30);var logs=await db.AuditLogs.Where(x=>x.OccurredAt<cutoff).ToListAsync(ct);db.AuditLogs.RemoveRange(logs);await db.SaveChangesAsync(ct);}}
