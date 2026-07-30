# ADR 0002: Introduce a transactional outbox before production

Status: Proposed

The current upload flow commits file metadata and then publishes RabbitMQ work. A failure between those operations can leave a queued record without a message. Before production, store an outbox record in the same PostgreSQL transaction, publish it from a background dispatcher with confirms, and mark it delivered idempotently. Reconciliation should detect stale queued records.
