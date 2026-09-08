Trace one photo-upload transaction end-to-end through MyTravels and find every
place where, if a step fails, the failure can't be correlated back to the
originating upload. Run this as an Explore agent (read-only investigation) —
don't fix anything, just report gaps.

Background, so the investigation doesn't need to rediscover the architecture:
a photo upload starts at `api` (`src/api/mytravels.api`, port 5101) or the
`mcp` server (`src/api/mytravels.mcp`, port 5103) via
`IPointOfInterestService`/`IMapsService`, writes to PostgreSQL, and publishes
to up to three RabbitMQ fanout exchanges — `resize-image`,
`append-formatted-address`, `append-image-tags` — consumed by the `messaging`
worker (`src/messaging/mytravels.messaging`, port 5102), which writes results
to MinIO/PostgreSQL. A 30-minute sweeper
(`AppendFormattedAddressSweeper.cs`) retries failed geocoding. `api`,
`messaging`, and `mcp` all export OTel traces/metrics via OTLP; `web` ships
browser RUM.

Read the actual code — don't guess — and inspect at least:
- The upload/POI-creation and update endpoints in `src/api/mytravels.api`
  (controller → `IPointOfInterestService`)
- The RabbitMQ publish path (`MessagePublisher` or equivalent, likely under
  `src/common/`) — message headers, AMQP `CorrelationId`, whether trace
  context is injected
- `src/common/.../MessageSubscriberBase.cs` (or equivalent base class every
  subscriber derives from) — what happens on exception: is it logged with the
  POI id? Is a failure message published? Does it nack-and-requeue forever, or
  is there a retry cap / dead-letter path?
- Each concrete subscriber in `src/messaging/mytravels.messaging/` (resize,
  geocode, tag-generation, and any others) — compare their error handling to
  each other; note any subscriber missing a catch block entirely
- The sweeper/cron base class and the sweeper itself — does one failing item
  abort the whole batch? Does a retry have any way to reference the original
  upload's correlation id or trace?
- `src/api/mytravels.mcp/Tools/` — does the MCP path publish the same
  messages as the REST path, with the same instrumentation? (Verify what
  tools actually exist here against what any docs claim — they can drift.)
- OTel setup (`Program.cs` in `api` and `messaging`) — is there an
  `ActivitySource` registered for RabbitMQ publish/consume, or only for
  ASP.NET Core / HTTP client / the database driver?
- Error-response middleware in `api` — is the error id returned to the caller
  derived from the active trace, or an unrelated random id?
- Any retry policies around external calls in the upload path (geocoding,
  image processing) — do retries log anything, or fail silently until they
  give up?

Only report a gap if you've read the file and can cite a real line number —
never fabricate one. Prefer specific, falsifiable claims ("no catch block
between lines X-Y" / "header X is never read on the consumer side") over
general observations ("error handling could be improved").

Output ONLY a JSON array, no prose before or after, no markdown fences. Each
element:
```
{
 "type": "message | trace | log | endpoint",
 "class": "<file name the gap is in, e.g. AppendFormattedAddress.cs>",
 "line": <real line number in that file>,
 "name": "<exchange, queue, span, or endpoint name involved>",
 "reason": "<concrete gap found and what should happen instead — not a generic suggestion>"
}
```

Where:
- `message` — a RabbitMQ publish/consume/retry/dead-letter gap
- `trace` — missing or broken OTel span / trace-context propagation
- `log` — a failure path that's logged without enough structured context to
  find it later, or not logged at all
- `endpoint` — a REST or MCP endpoint gap (missing instrumentation, docs
  claiming a capability the code doesn't have, etc.)

Cover the full lifecycle — publish, broker, consume/failure, MCP path, sweeper
retry — not just one file. One finding per distinct gap.
