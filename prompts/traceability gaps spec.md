# Upload-transaction traceability gaps

**Status:** Approved for planning
**Date:** 2026-09-07

## Problem

A single photo upload spans `api` (or `mcp`) → PostgreSQL → two/three RabbitMQ
fanout exchanges (`resize-image`, `append-formatted-address`, `append-image-tags`)
→ the `messaging` worker → MinIO/PostgreSQL, plus a 30-minute sweeper that
retries failed geocoding. `api`, `messaging`, and `mcp` all export OTel traces,
but a distributed-tracing / gap analysis of the actual code (see findings below,
all verified against `src/`) found that once a message crosses into RabbitMQ,
its connection back to the originating request is lost, and several failure
paths have no terminal, observable signal at all — they either loop forever or
disappear silently. This spec addresses each gap directly.

## Current state (verified findings)

- **No trace-context propagation across RabbitMQ.** `MessagePublisher.PublishAsync`
  (`mytravels.common/Services/MessagePublisher.cs:27-34`) calls `BasicPublishAsync`
  with no `IBasicProperties`/headers — no W3C `traceparent` is attached.
  `MessageSubscriberBase<T>`'s `ReceivedAsync` handler
  (`mytravels.common/Services/MessageSubscriberBase.cs:58-87`) never reads
  `ea.BasicProperties.Headers` to extract one. Neither `api`'s nor `messaging`'s
  OTel setup registers an `ActivitySource` for RabbitMQ publish/consume
  (`mytravels.api/Program.cs:29-32`, `mytravels.messaging/Program.cs:28-31` only
  add ASP.NET Core, HttpClient, and `Npgsql`), so the fanout to all three
  exchanges is invisible to tracing regardless of headers.
- **No terminal failure signal; infinite requeue instead.** The generic catch in
  `MessageSubscriberBase.cs:82-86` logs `"Error processing message."` and
  unconditionally `BasicNackAsync(..., requeue: true)` — no retry cap, no
  dead-letter routing, no `<exchange>-failed` message is ever published anywhere
  in the codebase. A permanently-failing message (bad geocoding input, corrupt
  image) retries forever with only a repeating generic log line as evidence.
- **Unstructured failure logs.** `AppendFormattedAddress.cs:54` and
  `AppendImageTags.cs:71` both log `LogError(ex, "Error processing X message")`
  without the `PointOfInterestId` or `CorrelationId` as structured fields, so a
  specific upload's failure can't be found by searching on its id.
- **`ResizeImage.cs` has no catch block at all** (`ResizeImage.cs:30-72`, only
  `try`/`finally`) — a resize failure (bad image, MinIO write error) is logged
  only by the generic base-class catch, with no `PointOfInterestId` context.
- **Inconsistent `CorrelationId`.** `CreatePointOfInterestAsync`
  (`PointOfInterestService.cs:114-117`) mints one `Guid` and shares it across all
  three publishes. `UpdatePointOfInterestAsync`
  (`PointOfInterestService.cs:84`) publishes to `resize-image` with no
  `CorrelationId` set (defaults to `Guid.Empty`) — the "add a photo to an
  existing POI" path has no correlation id at all.
- **Sweeper batches fail atomically and lose correlation.**
  `AppendFormattedAddressSweeper.DoWorkAsync`
  (`AppendFormattedAddressSweeper.cs:36-43`) has no per-point try/catch inside
  the `foreach` — one point's `GetAddressAsync` throwing aborts the whole batch,
  caught only by the outer handler at `AppendFormattedAddressSweeper.cs:45-49`,
  which then rethrows. `PointOfInterest` (`mytravels.contract/Entities/PointOfInterest.cs`)
  has no `CorrelationId` column, so even a successful sweep retry can't be tied
  back to the original upload's correlation id / trace.
- **First cron run is unguarded.** `CronJobBase.ExecuteAsync`
  (`mytravels.common/Services/CronJobBase.cs:19`) calls `await DoWorkAsync();`
  *before* the `try`/`catch` that only wraps the periodic loop
  (`CronJobBase.cs:22-29`). An exception on that first run crashes the whole
  `messaging` host process, silently killing all three subscribers.
- **Silent message loss on publish.** `MessagePublisher.PublishAsync` uses
  `mandatory: false` with no publisher confirms
  (`MessagePublisher.cs:27-34`). If a queue isn't bound yet (e.g. mid-rollout),
  RabbitMQ drops the message with no application-level signal whatsoever.
- **`ErrorId` isn't trace-linked.** `ApiExceptionMiddleware`
  (`HandleServerErrorAsync`, `ApiExceptionMiddleware.cs:61`, and
  `HandleClientErrorAsync`, `ApiExceptionMiddleware.cs:79`) mints
  `Guid.NewGuid().ToString("N")` as the returned `ErrorId`, unrelated to the
  request's `Activity`/`TraceId` — support has to correlate by manual log search
  instead of jumping into the trace.
- **Silent geocoding retries.** `GoogleMapsService`'s `AsyncRetryPolicy`
  (`GoogleMapsService.cs:23-26`, mirrored in `OpenStreetMapsService.cs:25-28`)
  has no `onRetry` callback — transient retries leave no log/metric trace.
- **Documentation drift, not a code gap:** `CLAUDE.md` and `SPEC.md` describe
  `mytravels.mcp` exposing `upload_photo` / `upload_photo_with_coordinates`
  tools. Verified via `Program.cs:76-77` and `Tools/PointOfInterestMcpTools.cs:21`
  that only `search_pointofinterest` and `search_place` are registered — no
  upload path exists through MCP today. This is a docs correction, not a
  traceability fix; see Out of scope.

## Design decisions

1. **Trace propagation uses the existing W3C `Activity.Id`, not a new
   dependency.** ASP.NET Core already sets `Activity.DefaultIdFormat =
   ActivityIdFormat.W3C`, so `Activity.Current?.Id` is already a valid
   `traceparent` string. Publish it as a message header; extract it on consume
   and start a linked `Activity` via `ActivitySource.StartActivity(name,
   ActivityKind.{Producer,Consumer}, parentId: header)`. No OpenTelemetry
   `Propagators` package needed.
2. **One shared `ActivitySource`, registered as an OTel source in both `api`
   and `messaging`.** Name it `"MyTravels.RabbitMQ"` so both publish (Producer)
   and consume (Consumer) spans appear under one source, added via
   `.AddSource("MyTravels.RabbitMQ")` next to the existing `.AddSource("Npgsql")`
   calls. No collector/manifest changes needed — the OTLP exporter already
   ships whatever spans are recorded.
3. **Failure signaling is bounded retry + explicit `-failed` message, not
   infra-level dead-lettering.** Track an `x-retry-count` header, incremented on
   each nack. Below a threshold (3, matching `GoogleMapsService`'s existing
   `_maxRetryAttempts` convention), nack+requeue as today. At/above threshold,
   publish a `<exchange>-failed` message (carrying `PointOfInterestId`,
   `CorrelationId`, and the exception message) to a new fanout exchange, then
   `BasicAckAsync` the original to stop the loop. This keeps the fix inside
   `MessageSubscriberBase` (one place) rather than duplicating retry logic in
   three subscribers, and doesn't require RabbitMQ DLX configuration in any
   stage's infra.
4. **`CorrelationId` becomes a persisted column**, not just an in-flight message
   field, so sweeper retries and any future support lookup can find it after
   the initiating trace has ended. Set from the same `Guid` already minted in
   `CreatePointOfInterestAsync`; reused (not re-minted) by
   `UpdatePointOfInterestAsync`'s `resize-image` publish.
5. **Structured logging fields, not new logging infra.** Every subscriber catch
   block gets `PointOfInterestId` and `CorrelationId` as structured
   `LogError` parameters — consistent with the pattern already used elsewhere
   (`ApiExceptionMiddleware.cs:68` already does `{ErrorId} -- {ErrorMessage}`).
6. **MCP upload tools are not built as part of this spec.** They're a separate
   feature (nothing to trace yet, since nothing publishes from `mcp` today).
   This spec only corrects the stale docs so future gap analyses don't assume a
   path that doesn't exist.

## Changes

### Contract (`mytravels.contract`)

- `ExchangeNames.cs`: add three new constants:
  ```csharp
  public const string ResizeImageFailed = "resize-image-failed";
  public const string AppendFormattedAddressFailed = "append-formatted-address-failed";
  public const string AppendImageTagsFailed = "append-image-tags-failed";
  ```
- New `FailedMessage` type (`mytravels.contract/Messages/`), implementing
  `IMessage`: `{ Guid CorrelationId; int PointOfInterestId; string
  OriginalExchange; string ErrorMessage; DateTime FailedAt; }`.
- `PointOfInterest.cs`: add `public Guid? CorrelationId { get; set; }`.

### Domain (`mytravels.domain`)

- New EF Core migration `AddPointOfInterestCorrelationId` (nullable `Guid?`
  column), following the existing migration convention used for the
  `Description` column.

### Common (`mytravels.common/Services`)

- **`MessagePublisher.cs`**: add an `ActivitySource` (`"MyTravels.RabbitMQ"`),
  start a `Producer`-kind `Activity` named `{exchange} publish` around the
  publish call, build `IBasicProperties` with:
  - `Headers["traceparent"] = Activity.Current?.Id`
  - `CorrelationId` (AMQP property) set from the message's own
    `CorrelationId` field, for RabbitMQ-management-UI-level correlation
  - Switch to publisher confirms (`ExchangeDeclareAsync` unchanged;
    `ConfirmSelectAsync` + await the confirm) and log a warning if a publish is
    nacked/returned unroutable, so a dropped message is no longer silent.
- **`MessageSubscriberBase.cs`**: in `ReceivedAsync`
  (currently `MessageSubscriberBase.cs:58-87`):
  - Extract `traceparent` from `ea.BasicProperties.Headers` and start a
    `Consumer`-kind `Activity` from the same `ActivitySource`, linked via
    `parentId`, before calling `ProcessMessageAsync`.
  - Read/increment an `x-retry-count` header (default 0). On exception: if
    `retryCount < 3`, nack+requeue as today (unchanged log text, but now
    including the retry count); otherwise, publish to the exchange's `-failed`
    counterpart (a new `Func<T, string>` or convention-based
    `{exchangeName}-failed` derivation passed into the base constructor) with
    the exception message, log at `Error` that the message is being
    dead-lettered after N attempts, and `BasicAckAsync` to remove it from the
    working queue.
  - This requires `MessageSubscriberBase<T>`'s constructor to also declare the
    `-failed` exchange (fanout, no bound queue required — consumers are
    optional/future) so publishing to it never fails on an undeclared exchange.
- **`CronJobBase.cs`**: wrap the first call at line 19 in the same `try`/`catch`
  the loop already uses (extract the body of the existing `catch` into a small
  private method, call it around both the initial and periodic invocations) so
  a first-run exception logs instead of crashing the host.

### API (`mytravels.api`)

- **`Program.cs`**: add `.AddSource("MyTravels.RabbitMQ")` next to the existing
  `.AddSource("Npgsql")` (lines 29-32).
- **`ApiExceptionMiddleware.cs`**: change `Id = Guid.NewGuid().ToString("N")`
  (lines 61 and 79) to prefer the active trace: `Id =
  System.Diagnostics.Activity.Current?.TraceId.ToString() ??
  Guid.NewGuid().ToString("N")` — support can paste the `ErrorId` straight into
  the tracing backend's trace search when a trace was recorded, falling back to
  a random id only if `Activity.Current` is somehow null.
- **`PointOfInterestService.cs`**:
  - `CreatePointOfInterestAsync` (lines 111-119): set `point.CorrelationId =
    correlationId` before/at insert so it's persisted, not just carried on the
    in-flight messages.
  - `UpdatePointOfInterestAsync` (line 84): reuse `point.CorrelationId` (already
    persisted from creation) on the `resize-image` publish instead of leaving
    it defaulted to `Guid.Empty`; if null (pre-migration rows), mint a new one
    and persist it back onto the point at the same time.

### Messaging (`mytravels.messaging`)

- **`Program.cs`**: add `.AddSource("MyTravels.RabbitMQ")` next to
  `.AddSource("Npgsql")` (lines 28-31).
- **`AppendFormattedAddress.cs:54`**: change the catch to
  `_logger.LogError(ex, "Error processing AppendFormattedAddress message for POI {PointOfInterestId}, correlation {CorrelationId}", obj.PointOfInterestId, obj.CorrelationId);` — same
  exception/rethrow behavior otherwise (retry/dead-letter handling now lives in
  the base class per the design decision above).
- **`AppendImageTags.cs:71`**: identical structured-logging change.
- **`ResizeImage.cs`**: add a `catch (Exception ex)` block around lines 30-67
  (currently `try`/`finally` only) that logs
  `"Error processing ResizeImage message for POI {PointOfInterestId}, correlation {CorrelationId}"`
  and rethrows, so it participates in the same retry/dead-letter path as the
  other two subscribers instead of falling through to the base class's
  unlabeled catch.
- **`AppendFormattedAddressSweeper.cs`**: move the `foreach` body (lines 36-43)
  into a per-point `try`/`catch` that logs
  `"Sweeper failed to geocode POI {PointOfInterestId}, correlation {CorrelationId}"`
  (using the now-persisted `point.CorrelationId`) and `continue`s instead of
  letting one failure abort the batch and bubble to the outer catch/rethrow at
  lines 45-49.

### Maps services (`mytravels.common/Services`)

- **`GoogleMapsService.cs`** and **`OpenStreetMapsService.cs`**: add an
  `onRetry` callback to the existing `WaitAndRetryAsync` policies (lines 23-26
  / 25-28) that logs the attempt number and exception at `Warning` — requires
  injecting `ILogger` into both services (not currently a constructor
  dependency).

### Documentation

- `CLAUDE.md` and `SPEC.md`: remove/correct the claim that `mytravels.mcp`
  exposes `upload_photo` / `upload_photo_with_coordinates` tools; state that it
  currently exposes `search_pointofinterest` and `search_place` only.

### Diagrams — `drawio/architecture.drawio`

- **Monitoring** page: add the new `MyTravels.RabbitMQ` producer/consumer spans
  to the OTLP/telemetry arrows already drawn for `api` and `messaging`.
- **Application Architecture** page: add the three `*-failed` exchanges as
  small dead-end fanout icons off each existing subscriber box, so the
  failure path is visible alongside the happy path.

## Out of scope

- Implementing `upload_photo` / `upload_photo_with_coordinates` MCP tools
  (separate feature; this spec only fixes the docs that incorrectly claim they
  exist).
- Consuming the new `*-failed` exchanges (e.g. an alerting subscriber, a
  Grafana panel on queue depth). This spec only makes the failure observable
  and stops the infinite-requeue loop; wiring an actual alert is a follow-up.
- RabbitMQ-level dead-letter-exchange (DLX) configuration as an alternative to
  the application-level `x-retry-count` approach — rejected in favor of the
  simpler in-code approach (design decision 3) to avoid touching queue
  declare/infra across all five stages.
- Backfilling `CorrelationId` for `PointOfInterest` rows created before this
  migration ships (column is nullable; historical rows stay `null`).
- `Baggage`/`tracestate` propagation — only `traceparent` is propagated;
  richer context propagation is a future enhancement if needed.
- Publisher-confirms performance impact assessment under load — flagged as a
  behavior change worth a quick load-test before merging, not designed here.
