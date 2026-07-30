using FileProcessing.Application;
using FileProcessing.Infrastructure;
using FileProcessing.Worker;
using Serilog;
var builder=Host.CreateApplicationBuilder(args);builder.Services.AddSerilog(x=>x.WriteTo.Console());builder.Services.AddApplication(builder.Configuration).AddInfrastructure(builder.Configuration);builder.Services.AddHostedService<QueueWorker>();var host=builder.Build();await host.RunAsync();
