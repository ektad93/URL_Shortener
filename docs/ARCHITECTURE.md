# Architecture and quality plan

## Data model

`short_urls` stores the short code, destination, lifecycle state, timestamps, and
aggregate counters. `click_events` stores bounded user-agent/referrer metadata
and timestamp for future reporting. The short code has a unique index, and event
lookups are indexed by URL and timestamp.

## Redirect flow

1. Validate the code shape.
2. Load the URL by indexed code.
3. Reject missing, disabled, or expired records.
4. Record a bounded click event and update aggregates.
5. Return a temporary redirect.

## Error strategy

Validation errors use FastAPI's `422` response. Missing records return `404`;
expired/disabled records return `410`; persistence failures return `503` without
exposing database details.

## Scaling path

Move to PostgreSQL, add a cache for hot redirects, enqueue click events through a
durable broker, add rate limiting at the edge, and expose metrics/traces. The
service/repository split keeps those changes localized.

