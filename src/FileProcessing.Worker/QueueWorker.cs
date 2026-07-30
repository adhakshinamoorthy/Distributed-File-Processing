using System.Text.Json;
using FileProcessing.Application;
using FileProcessing.Contracts;
using FileProcessing.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FileProcessing.Worker;
public sealed class QueueWorker(IServiceScopeFactory scopes,IOptions<RabbitOptions> settings,ILogger<QueueWorker> logger):BackgroundService
{
    private IConnection? connection;private IChannel? channel;private readonly RabbitOptions o=settings.Value;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory=new ConnectionFactory{HostName=o.Host,UserName=o.UserName,Password=o.Password,AutomaticRecoveryEnabled=true,ConsumerDispatchConcurrency=(ushort)Math.Min(ushort.MaxValue,Environment.ProcessorCount)};connection=await factory.CreateConnectionAsync(stoppingToken);channel=await connection.CreateChannelAsync(cancellationToken:stoppingToken);
        await channel.ExchangeDeclareAsync("file-processing.dlx",ExchangeType.Direct,true,false,cancellationToken:stoppingToken);var args=new Dictionary<string,object?>{{"x-max-priority",10},{"x-dead-letter-exchange","file-processing.dlx"}};await channel.QueueDeclareAsync(o.Queue,true,false,false,args,cancellationToken:stoppingToken);await channel.QueueDeclareAsync(o.Queue+".dead",true,false,false,cancellationToken:stoppingToken);await channel.QueueBindAsync(o.Queue+".dead","file-processing.dlx",o.Queue,cancellationToken:stoppingToken);await channel.BasicQosAsync(0,o.Prefetch,false,stoppingToken);
        var consumer=new AsyncEventingBasicConsumer(channel);consumer.ReceivedAsync+=HandleAsync;await channel.BasicConsumeAsync(o.Queue,false,consumer,stoppingToken);await Task.Delay(Timeout.Infinite,stoppingToken);
    }
    private async Task HandleAsync(object sender,BasicDeliverEventArgs e)
    {
        if(channel is null)return;try{var job=JsonSerializer.Deserialize<FileJob>(e.Body.Span)??throw new InvalidDataException("Invalid job payload.");using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<IFileProcessingService>().ProcessAsync(job,e.CancellationToken);await channel.BasicAckAsync(e.DeliveryTag,false,e.CancellationToken);}catch(OperationCanceledException)when(e.CancellationToken.IsCancellationRequested){}catch(Exception ex){logger.LogError(ex,"File job {MessageId} failed",e.BasicProperties.MessageId);await channel.BasicNackAsync(e.DeliveryTag,false,false,e.CancellationToken);}}
    public override async Task StopAsync(CancellationToken cancellationToken){if(channel is not null)await channel.CloseAsync(cancellationToken);if(connection is not null)await connection.CloseAsync(cancellationToken);await base.StopAsync(cancellationToken);}public override void Dispose(){channel?.Dispose();connection?.Dispose();base.Dispose();}
}
