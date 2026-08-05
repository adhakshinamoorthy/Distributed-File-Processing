# Verification

CI restores, checks formatting, builds in Release, runs deterministic tests, audits NuGet dependencies, and validates the Compose model. Current tests cover domain transitions and contract serialization; they do not prove a live PostgreSQL/RabbitMQ/MinIO/ClamAV/Tesseract workflow.

For an end-to-end check, copy `.env.example` to `.env`, replace every value, start the stack, upload one clean image and the standard EICAR test file, and verify API state, PostgreSQL history, MinIO artifacts, queue acknowledgement, and worker logs. Record the date and environment when publishing runtime results.
