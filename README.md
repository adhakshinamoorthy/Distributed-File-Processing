# Distributed File Processing Platform

A local-first .NET 10 platform that streams uploads to MinIO, records state in PostgreSQL, publishes durable RabbitMQ jobs, and processes files in horizontally scalable workers with ClamAV, Tesseract OCR, thumbnails, notifications, auditing, retries, Hangfire, Redis, structured logs, metrics, traces, and health checks.

## Architecture and workflow

```text
Client -> API -> validation/checksum -> MinIO original -> PostgreSQL -> RabbitMQ
                                                                  |
                         scalable Worker pool <-------------------+
                              |
             ClamAV -> Tesseract -> thumbnail -> MinIO artifacts
                              |
                PostgreSQL audit/status -> webhook notification

Upload -> Validated -> Queued -> Virus scan -> OCR -> Thumbnail -> Stored
       -> NotificationSent -> Completed (or Failed/VirusDetected/Retrying)
```

The API owns ingestion and queries. Workers own asynchronous processing. RabbitMQ manual acknowledgements, publisher confirmations, persistent messages, priority, prefetch, a dead-letter exchange, idempotent completed-job handling, and container replicas allow safe parallel work. Hangfire runs retry and cleanup schedules inside the API. PostgreSQL is authoritative; MinIO stores originals, thumbnails, and OCR text.

## Repository

```text
src/FileProcessing.Api             HTTP, auth, Hangfire, OpenTelemetry
src/FileProcessing.Application     processing use case and ports
src/FileProcessing.Domain          entities, status state machine, audit model
src/FileProcessing.Infrastructure  EF Core, RabbitMQ, MinIO, ClamAV, OCR
src/FileProcessing.Worker          queue consumer host
src/FileProcessing.Contracts       immutable transport records
src/FileProcessing.Shared          result primitives
tests/UnitTests                    domain tests
tests/IntegrationTests             contract/integration test project
docker/                            reserved operational assets
```

## Prerequisites

- Docker Desktop with Compose v2
- Optional host development: .NET SDK 10.0.302 or newer
- About 6 GB free RAM while ClamAV initializes its signatures

The checked-in credentials are local-development values. Change `Authentication`, PostgreSQL, RabbitMQ, and MinIO secrets before sharing a reachable environment.

## Run everything

```powershell
docker compose up --build --scale worker=2
```

First startup can take several minutes because ClamAV downloads signatures. Wait until `docker compose ps` reports healthy dependencies, then open:

- Swagger: http://localhost:8080/swagger
- Health: http://localhost:8080/health
- Hangfire: http://localhost:8080/hangfire (loopback only)
- RabbitMQ: http://localhost:15672 (`fileprocessing` / `local_dev_password`)
- MinIO: http://localhost:9001 (`minioadmin` / `minioadmin`)

Stop without deleting data with `docker compose down`. To intentionally remove local data, run `docker compose down --volumes`.

## Authentication and uploads

Protected endpoints accept a JWT with issuer `FileProcessing`, audience `FileProcessing.Client`, and role `admin` or `processor`. For local calls, the API-key middleware accepts `X-Api-Key: local-development-api-key` as the `processor` role.

```powershell
curl.exe -X POST http://localhost:8080/files/upload `
  -H "X-Api-Key: local-development-api-key" `
  -F "file=@C:\files\sample.pdf;type=application/pdf"

curl.exe -X POST http://localhost:8080/files/upload/bulk `
  -H "X-Api-Key: local-development-api-key" `
  -F "uploads=@C:\files\one.txt;type=text/plain" `
  -F "uploads=@C:\files\two.png;type=image/png"

curl.exe -H "X-Api-Key: local-development-api-key" http://localhost:8080/files/status/FILE_ID
curl.exe -H "X-Api-Key: local-development-api-key" http://localhost:8080/files?page=1`&pageSize=25
curl.exe -X POST -H "X-Api-Key: local-development-api-key" http://localhost:8080/files/retry/FILE_ID
curl.exe -L -H "X-Api-Key: local-development-api-key" http://localhost:8080/files/download/FILE_ID -o downloaded.bin
```

DELETE requires an `admin` JWT. Downloads are allowed only after completion and redirect to a ten-minute MinIO signed URL.

## Validation and processing

Allowed types are PDF, DOCX, XLSX, TXT, PNG, JPEG, and TIFF. The API enforces configured size, extension, MIME type, SHA-256 duplicate detection, and signatures for PDF, Office ZIP, PNG, and JPEG before queueing. Upload content is streamed into a bounded buffer for hashing/signature validation and then into MinIO. Set `FileProcessing:MaxFileSizeBytes` to change the 100 MiB default.

Workers use ClamAV's INSTREAM protocol. Infected files stop at `VirusDetected`. Text files are read directly; other clean files invoke Tesseract. Image thumbnails preserve aspect ratio within `Thumbnails:Width` and `Height`. OCR text and thumbnails are written to MinIO. Configure `Notifications:WebhookUrl` to receive JSON events.

Transient consumer failures are negatively acknowledged to the dead-letter exchange. Operators can call the retry endpoint; Hangfire also republishes up to 100 failed jobs hourly. Attempts are capped by `FileProcessing:MaxRetries`. Completed jobs are idempotent. Increase throughput with `docker compose up --scale worker=4`; RabbitMQ prefetch controls per-consumer concurrency.

## Configuration

Settings live in each host's `appsettings.json` and can be overridden with standard ASP.NET Core environment variables such as `ConnectionStrings__PostgreSql`, `RabbitMq__Host`, or `Authentication__ApiKey`. Major sections are `FileProcessing`, `RabbitMq`, `Minio`, `ClamAV`, `OCR`, `Thumbnails`, `Notifications`, `Authentication`, `Serilog`, and `OpenTelemetry`.

The API creates the local schema at startup. For a versioned deployment workflow, install `dotnet-ef`, add `Microsoft.EntityFrameworkCore.Design` to the API, create a migration from the infrastructure context, and apply it before starting hosts:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef migrations add InitialCreate --project src/FileProcessing.Infrastructure --startup-project src/FileProcessing.Api
dotnet ef database update --project src/FileProcessing.Infrastructure --startup-project src/FileProcessing.Api
```

## Build and test

```powershell
dotnet restore DistributedFileProcessing.slnx
dotnet build DistributedFileProcessing.slnx --no-restore
dotnet test DistributedFileProcessing.slnx --no-build
docker compose config --quiet
```

Unit tests verify state/history invariants. The integration project is wired to the API and currently checks serialized queue contracts. A live end-to-end check requires the Compose dependencies: upload the EICAR test file and confirm `VirusDetected`, then upload a clean image and observe `Completed`, OCR/thumbnail objects in MinIO, history rows in PostgreSQL, and acknowledged messages in RabbitMQ.

## Operations and troubleshooting

Every transition creates processing history; uploads, retries, failures, completion, and deletion create correlation-indexed audit records. Send `X-Correlation-ID` as a GUID or use the generated value returned in the response header. Serilog emits structured request/worker logs. OpenTelemetry instruments HTTP, runtime, and outbound requests; point `OTEL_EXPORTER_OTLP_ENDPOINT` to a local collector to export traces and metrics.

- API waits or fails at startup: check `docker compose ps` and `docker compose logs postgres api`.
- Jobs remain queued: check `docker compose logs worker rabbitmq clamav`; ClamAV readiness is intentionally slow on first boot.
- OCR fails: verify the worker image built successfully and `tesseract --list-langs` includes `eng`.
- MinIO errors: verify port 9000 health and matching access keys in both hosts.
- Duplicate response: the same SHA-256 already exists; delete the record/object or upload different content.
- `401/403`: include the local API key, or use a correctly issued JWT and required role.
- Hangfire unavailable remotely: dashboard access is intentionally restricted to loopback.

## API routes

`POST /files/upload`, `POST /files/upload/bulk`, `GET /files/{id}`, `GET /files`, `GET /files/status/{id}`, `GET /files/download/{id}`, `POST /files/retry/{id}`, `DELETE /files/{id}`, and `GET /health`.
