using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FileProcessing.Application;
using FileProcessing.Contracts;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace FileProcessing.Infrastructure;

public sealed class StorageOptions { public const string Section="Minio"; public string Endpoint{get;init;}="localhost:9000";public string AccessKey{get;init;}="minioadmin";public string SecretKey{get;init;}="minioadmin";public string Bucket{get;init;}="files";public bool Secure{get;init;} }
public sealed class RabbitOptions { public const string Section="RabbitMq";public string Host{get;init;}="localhost";public string UserName{get;init;}="guest";public string Password{get;init;}="guest";public string Queue{get;init;}="file-processing";public ushort Prefetch{get;init;}=4; }
public sealed class ClamAvOptions { public const string Section="ClamAV";public string Host{get;init;}="localhost";public int Port{get;init;}=3310;public int TimeoutSeconds{get;init;}=120; }
public sealed class OcrOptions { public const string Section="OCR";public string Executable{get;init;}="tesseract";public string Language{get;init;}="eng";public int TimeoutSeconds{get;init;}=120; }
public sealed class ThumbnailOptions { public const string Section="Thumbnails";public int Width{get;init;}=320;public int Height{get;init;}=320; }

public sealed class MinioBlobStorage(IMinioClient client,Microsoft.Extensions.Options.IOptions<StorageOptions> settings):IBlobStorage
{
    private readonly StorageOptions o=settings.Value;
    private async Task EnsureAsync(CancellationToken ct){if(!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(o.Bucket),ct))await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(o.Bucket),ct);}
    public async Task PutAsync(string name,Stream content,string type,IReadOnlyDictionary<string,string>? metadata,CancellationToken ct){await EnsureAsync(ct);var args=new PutObjectArgs().WithBucket(o.Bucket).WithObject(name).WithStreamData(content).WithObjectSize(content.CanSeek?content.Length:-1).WithContentType(type);if(metadata is not null)args.WithHeaders(new Dictionary<string,string>(metadata));await client.PutObjectAsync(args,ct);}
    public async Task<Stream> GetAsync(string name,CancellationToken ct){var ms=new MemoryStream();await client.GetObjectAsync(new GetObjectArgs().WithBucket(o.Bucket).WithObject(name).WithCallbackStream(async (stream,token)=>await stream.CopyToAsync(ms,token)),ct);ms.Position=0;return ms;}
    public Task DeleteAsync(string name,CancellationToken ct)=>client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(o.Bucket).WithObject(name),ct);
    public Task<string> PresignedGetAsync(string name,TimeSpan lifetime,CancellationToken ct)=>client.PresignedGetObjectAsync(new PresignedGetObjectArgs().WithBucket(o.Bucket).WithObject(name).WithExpiry((int)lifetime.TotalSeconds));
}
public sealed class RabbitJobPublisher(Microsoft.Extensions.Options.IOptions<RabbitOptions> settings):IJobPublisher
{
    private readonly RabbitOptions o=settings.Value;
    public async Task PublishAsync(FileJob job,byte priority,CancellationToken ct){var factory=new ConnectionFactory{HostName=o.Host,UserName=o.UserName,Password=o.Password,AutomaticRecoveryEnabled=true};await using var connection=await factory.CreateConnectionAsync(ct);await using var channel=await connection.CreateChannelAsync(new CreateChannelOptions(true,true),ct);var args=new Dictionary<string,object?>{{"x-max-priority",10},{"x-dead-letter-exchange","file-processing.dlx"}};await channel.ExchangeDeclareAsync("file-processing.dlx",ExchangeType.Direct,true,false,null,false,ct);await channel.QueueDeclareAsync(o.Queue,true,false,false,args,false,ct);var props=new BasicProperties{Persistent=true,Priority=priority,MessageId=job.FileId.ToString(),CorrelationId=job.CorrelationId,ContentType="application/json"};await channel.BasicPublishAsync("",o.Queue,true,props,JsonSerializer.SerializeToUtf8Bytes(job),ct);}
}
public sealed class ClamAvScanner(Microsoft.Extensions.Options.IOptions<ClamAvOptions> settings):IVirusScanner
{
    private readonly ClamAvOptions o=settings.Value;
    public async Task<ScanOutput> ScanAsync(Stream file,CancellationToken ct){using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));using var client=new TcpClient();await client.ConnectAsync(o.Host,o.Port,timeout.Token);await using var network=client.GetStream();await network.WriteAsync("zINSTREAM\0"u8.ToArray(),timeout.Token);var buffer=new byte[8192];int read;while((read=await file.ReadAsync(buffer,timeout.Token))>0){await network.WriteAsync(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(read)),timeout.Token);await network.WriteAsync(buffer.AsMemory(0,read),timeout.Token);}await network.WriteAsync(new byte[4],timeout.Token);var responseBuffer=new byte[1024];var count=await network.ReadAsync(responseBuffer,timeout.Token);var response=Encoding.UTF8.GetString(responseBuffer,0,count).TrimEnd('\0');var infected=response.Contains(" FOUND",StringComparison.Ordinal);return new(infected,infected?response:null);}
}
public sealed class TesseractOcrProvider(Microsoft.Extensions.Options.IOptions<OcrOptions> settings):IOcrProvider
{
    private readonly OcrOptions o=settings.Value;
    public async Task<OcrOutput> ExtractAsync(Stream file,string contentType,CancellationToken ct){if(contentType=="text/plain"){using var reader=new StreamReader(file,Encoding.UTF8,true,leaveOpen:true);return new(await reader.ReadToEndAsync(ct),"plain-text");}var input=Path.GetTempFileName();var output=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{await using(var fs=File.Create(input))await file.CopyToAsync(fs,ct);using var process=Process.Start(new ProcessStartInfo(o.Executable,$"\"{input}\" \"{output}\" -l {o.Language}"){RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true})??throw new InvalidOperationException("Unable to start Tesseract.");using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));await process.WaitForExitAsync(timeout.Token);if(process.ExitCode!=0)throw new InvalidOperationException(await process.StandardError.ReadToEndAsync(timeout.Token));return new(await File.ReadAllTextAsync(output+".txt",ct),"tesseract");}finally{File.Delete(input);File.Delete(output+".txt");}}
}
public sealed class ImageThumbnailGenerator(Microsoft.Extensions.Options.IOptions<ThumbnailOptions> settings):IThumbnailGenerator
{
    private readonly ThumbnailOptions o=settings.Value;
    public async Task<ThumbnailOutput?> GenerateAsync(Stream file,string contentType,CancellationToken ct){if(!contentType.StartsWith("image/",StringComparison.Ordinal))return null;using var image=await Image.LoadAsync(file,ct);image.Mutate(x=>x.Resize(new ResizeOptions{Size=new(o.Width,o.Height),Mode=ResizeMode.Max}));await using var ms=new MemoryStream();await image.SaveAsync(ms,new JpegEncoder{Quality=80},ct);return new(ms.ToArray(),"image/jpeg",image.Width,image.Height,".jpg");}
}
public sealed class WebhookNotificationSender(HttpClient client,Microsoft.Extensions.Configuration.IConfiguration configuration):INotificationSender { public string Channel=>"Webhook";public async Task SendAsync(NotificationMessage message,CancellationToken ct){var url=configuration["Notifications:WebhookUrl"];if(string.IsNullOrWhiteSpace(url))return;using var response=await client.PostAsJsonAsync(url,message,ct);response.EnsureSuccessStatusCode();} }
