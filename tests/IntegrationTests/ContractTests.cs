using FileProcessing.Contracts;
namespace IntegrationTests;

public sealed class ContractTests { [Fact] public void File_job_round_trips() { var job = new FileJob(Guid.NewGuid(), "correlation", 2); var json = System.Text.Json.JsonSerializer.Serialize(job); Assert.Equal(job, System.Text.Json.JsonSerializer.Deserialize<FileJob>(json)); } }
