# Async image description & tagging

**Status:** Approved for planning
**Date:** 2026-09-03

## Problem

Image description/tagging (via `AnthropicImageDescriptionService`) currently runs
synchronously, triggered by a "Describe" button in `PoiDialog.tsx` that calls
`POST api/PointOfInterest/{id}/describe`. This was a testing scaffold. We're
productionizing it as an asynchronous, message-driven flow consistent with the
existing `resize-image` / `append-formatted-address` background jobs, so
description/tags are generated automatically at upload time with no user action
and no synchronous Claude call in the request path.

## Current state (for reference)

- `PointOfInterestController.cs:61-67` — `POST {id}/describe` → `ImageDescriptionDto`.
- `PointOfInterestService.cs:99-110` — `DescribeImageAsync`: loads POI, guards on
  `GeneratedBlobName`, reads base64 from `NewUploadedImagesContainer` (original
  bucket), calls `IImageDescriptionService.DescribeAsync`, returns the DTO inline.
- `PointOfInterestService.cs:112-131` (`CreatePointOfInterestAsync`) already
  publishes to two exchanges in parallel right after insert:
  ```csharp
  await _publisher.PublishAsync(ExchangeNames.AppendFormattedAddress, new PointOfInterestMessage { CorrelationId = Guid.NewGuid(), PointOfInterestId = id }, cancellationToken);
  await _publisher.PublishAsync(ExchangeNames.ResizeImage, new PointOfInterestMessage { PointOfInterestId = point.Id }, cancellationToken);
  ```
- `ExchangeNames` (`mytravels.contract/Constants/ExchangeNames.cs`) has
  `ResizeImage = "resize-image"` and `AppendFormattedAddress = "append-formatted-address"`.
- `MessageSubscriberBase<T>` (`mytravels.common/Services/MessageSubscriberBase.cs`)
  is the `IHostedService` base every subscriber derives from. `ResizeImage.cs` and
  `AppendFormattedAddress.cs` (`mytravels.messaging/`) are the concrete examples —
  both call `base(logger, configuration, ExchangeNames.X, ExchangeNames.X)` (queue
  name == exchange name), resolve dependencies from a fresh DI scope per message.
- `mytravels.messaging/Program.cs` already registers `IImageDescriptionService →
  AnthropicImageDescriptionService` and `IPointOfInterestService →
  PointOfInterestService` (lines 62-63) — the service-layer plumbing already
  exists in the messaging host, it's just unused today.
- `Tag` and `PointOfInterestTagAssociation` entities already exist, plus a
  stored-proc-backed write path: `UpdatePointOfInterestTagsAsync` (on
  `ICoreDbContext`) → `spUpdatePointOfInterestTags`, taking
  `PointOfInterestTag { int PointOfInterestId; string TagName; }`.
- `PointOfInterest` entity has **no** `Description` column today.
- **Verified:** `GeneratedBlobName` is set at POI creation time, not by
  `ResizeImage`. `ResizeImage.cs:51` reads the original from
  `NewUploadedImagesContainer/{GeneratedBlobName}` and writes the resized copy
  under the *same* blob name to `ResizedImagesContainer` (`ResizeImage.cs:60`).
  This confirms the parallel-trigger decision below is safe: the original image
  already exists by the time `AppendImageTags` fires, with no dependency on
  `ResizeImage` having run first.
- `AnthropicApiKey` is currently wired only onto the `api` service, in all of:
  `1-dockerize/.env` + `docker-compose.yml`, `2-dockerhub/.env.example` +
  `docker-compose.yml`, `3-kubernetes/manifests/api/1-secret.yaml` +
  `2-deployment.yaml`, `4-argocd/manifests/api/*`. It is absent everywhere on
  `messaging`.
- `3-kubernetes/runbook.ipynb` and `4-argocd/runbook.ipynb` reference "describe"
  (the manual button/endpoint) in their walkthrough steps and will need updating.
- **Flag, out of scope:** `1-dockerize/.env` contains what looks like a live,
  non-placeholder Anthropic API key committed to the repo. Not touched by this
  work; call out separately for rotation.

## Design decisions (confirmed)

1. **Trigger:** `AppendImageTags` is published from `CreatePointOfInterestAsync`,
   in parallel with `ResizeImage` and `AppendFormattedAddress` (not chained after
   either). No trigger on update — only on creation.
2. **Result delivery:** No polling, no new endpoint for "in progress" state.
   `Description` is added to `PointOfInterestDto` / `GET api/PointOfInterest`;
   `PoiDialog` renders it (and `Tags`) straight from the POI data it already has.
   If generation hasn't completed yet, the fields are simply absent until the
   list is next fetched (dialog reopen / refresh).
3. **Tag persistence:** reuse `UpdatePointOfInterestTagsAsync` (existing
   stored-proc convention), not bespoke EF Core writes.
4. **Image source:** subscriber reads from `NewUploadedImagesContainer` (the
   original bucket), same as today's sync code — it cannot assume `ResizeImage`
   has completed, since the two now run independently/in parallel.
5. **API cleanup:** `AnthropicApiKey` / `IImageDescriptionService` wiring is
   *moved* off `api` entirely (dead after this change) and onto `messaging`
   (newly wired) — not duplicated on both.

## Changes

### Backend — contract / domain

- `ExchangeNames.cs`: add `public const string AppendImageTags = "append-image-tags";`
- `PointOfInterestService.CreatePointOfInterestAsync`: add a third publish call:
  ```csharp
  await _publisher.PublishAsync(ExchangeNames.AppendImageTags, new PointOfInterestMessage { PointOfInterestId = point.Id }, cancellationToken);
  ```
  (reuses `PointOfInterestMessage` as-is — only `PointOfInterestId` is needed.)
- Remove `DescribeImageAsync` from `IPointOfInterestService` and
  `PointOfInterestService` (lines 99-110). `ImageDescriptionDto` and
  `IImageDescriptionService` stay (still used, just by the subscriber now).
- `PointOfInterest` entity: add nullable `string? Description` property, mapped
  as an unbounded `text` column (no `[StringLength]`) — Claude descriptions are
  free text of variable length, unlike `Tag.Name` which has a 30-char cap for a
  different reason (short label constraint).
- `PointOfInterestDto`: add `Description`. Map it in the list query used by
  `GET api/PointOfInterest`.
- New EF Core migration: `dotnet ef migrations add AddPointOfInterestDescription`
  in `mytravels.domain` (adds the `Description` column). Follow existing
  migration folder/tooling conventions in `mytravels.domain/Migrations`.

### Backend — API

- `PointOfInterestController.cs`: remove the `POST {id}/describe` action
  (lines 61-67) entirely.

### Backend — messaging

- New file `mytravels.messaging/AppendImageTags.cs`:
  ```csharp
  public class AppendImageTags : MessageSubscriberBase<PointOfInterestMessage>
  {
      // ctor mirrors ResizeImage.cs: ILogger<AppendImageTags>, IConfiguration, IServiceScopeFactory
      // base(logger, configuration, ExchangeNames.AppendImageTags, ExchangeNames.AppendImageTags)

      protected override async Task ProcessMessageAsync(PointOfInterestMessage obj, CancellationToken cancellationToken)
      {
          // fresh DI scope per message (ResizeImage.cs pattern)
          // resolve ICoreDbContext, IObjectStorageService, IImageDescriptionService
          // 1. load PointOfInterest by obj.PointOfInterestId; throw/skip if not found
          // 2. guard on GeneratedBlobName (skip if empty, same as today's check)
          // 3. base64 = await objectStorageService.GetBase64Async(BucketNames.NewUploadedImagesContainer, point.GeneratedBlobName, cancellationToken)
          // 4. result = await imageDescriptionService.DescribeAsync(base64, cancellationToken)
          // 5. set point.Description = result.Description; SaveChangesAsync
          // 6. await context.UpdatePointOfInterestTagsAsync(point.Id, result.Tags, cancellationToken) — via the existing PointOfInterestTag-based convention
      }
  }
  ```
  Ported logic is the current body of `PointOfInterestService.DescribeImageAsync`
  (lines 99-110), split into: fetch/guard/describe (as today) + two persistence
  steps that didn't exist in the sync path (Description column write, tags write).
- `mytravels.messaging/Program.cs`: register the hosted service —
  `builder.Services.AddHostedService<AppendImageTags>();` alongside the existing
  `ResizeImage`/`AppendFormattedAddress` registrations.
- No new DI registrations needed for `IImageDescriptionService`/
  `IPointOfInterestService` — already present (lines 62-63).

### Frontend

- `PoiDialog.tsx`: remove the `DescribeState` type, the `describe` state + its
  reset effect, `handleDescribe`, the button, and the describing/error blocks.
  Render `poi.description` (paragraph) and `poi.tags` (pills) directly from the
  `poi` prop wherever the old `describe.result` was rendered.
- `client.ts`: remove `describePointOfInterestImage`.
- `types.ts`: add `description: string | null` to the `PointOfInterest`
  interface. `ImageDescription` type can be removed if nothing else uses it
  (verify at implementation time).

### Infra — all five stages

`mcp` is unaffected (no describe tool exists there today — confirmed nothing to
touch).

| Stage | Remove from `api` | Add to `messaging` |
|---|---|---|
| `0-local` | n/a (source-run, uses `appsettings.json` placeholder) | n/a |
| `1-dockerize` | `AnthropicApiKey` env wiring in `docker-compose.yml` api service | same env var, messaging service; keep `.env`/`.env.example` key (now consumed by messaging) |
| `2-dockerhub` | `AnthropicApiKey` env wiring in `docker-compose.yml` api service | same env var, messaging service |
| `3-kubernetes` | `AnthropicApiKey` from `manifests/api/1-secret.yaml` + `2-deployment.yaml` | add key to `manifests/messaging/1-secret.yaml` + env/`secretKeyRef` in `manifests/messaging/2-deployment.yaml` |
| `4-argocd` | same, `manifests/api/deployment.yaml` + secret | same, `manifests/messaging/deployment.yaml` + secret |

- No RabbitMQ infra/config changes needed anywhere — fanout exchanges are
  declared at runtime by `MessageSubscriberBase`/`MessagePublisher`, same as the
  existing two.
- Migration: no wiring changes — the new column ships via the existing
  `mytravels.migration` one-shot job already deployed in stages 3-4.
- Update `3-kubernetes/runbook.ipynb` and `4-argocd/runbook.ipynb`: remove/replace
  the describe-button walkthrough step(s) with a step reflecting the new
  automatic flow (e.g. verify `Description`/`Tags` appear on a POI after upload,
  without manual action).

### Diagrams — `drawio/architecture.drawio`

- **Application Architecture** page: add a third subscriber box
  (`Append Image Tags Subscriber :5102`) and its `append-image-tags` queue icon,
  matching the existing two-subscriber pattern. Add a new box/arrow for the
  Anthropic Claude API (not depicted at all today, despite the feature already
  existing) from the new subscriber. No removal needed on the `api`/`Web` boxes
  since the describe flow was never drawn there.
- **Monitoring** page: mirror the OTLP/telemetry arrow pattern used for the
  other two subscribers, for the new third one.

## Out of scope

- Rotating the live-looking Anthropic key found in `1-dockerize/.env`.
- Cleaning up the apparently-dead `ContentSafetyEndpoint`/`ContentSafetyKey`/
  `GoogleApiKey` entries in `3-kubernetes/manifests/messaging/1-secret.yaml`.
- Any retry/dead-letter handling beyond what `MessageSubscriberBase` already
  provides (nack + requeue on exception).
- Backfilling `Description`/`Tags` for POIs created before this change.
