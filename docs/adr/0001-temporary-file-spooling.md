# ADR 0001: Spool uploads to temporary storage

Status: Accepted

The API copies each accepted upload to an asynchronously opened temporary file, computes its checksum and signature from that seekable stream, then sends it to object storage. This avoids allocating up to the full upload limit in managed memory. The trade-off is temporary-disk capacity and I/O; deployments must size and monitor ephemeral storage.
