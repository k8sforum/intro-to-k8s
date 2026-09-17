# SOLR-backed point-of-interest search

**Status:** Approved for planning
**Date:** 2026-09-13

## Problem

POI search today is a single PostgreSQL `ILIKE` over one column. `CoreDbContext.SearchPointsOfInterestByFormattedAddressAsync`
(`mytravels.domain/CoreDbContext.cs:44-70`) matches `%term%` against `FormattedAddress`
only - it cannot search the AI-generated `Description`, cannot search tags, has no
date filtering, no relevance ranking (results come back in table order), and no
stemming or tokenisation, so "cape town" does not match "Cape Town, South Africa"
any better than a substring does.

Tag search exists but on a separate path: `GetPointsOfInterestByTagAsync`
(`CoreDbContext.cs:35-36`) calls `spGetPointOfInterestByTagName`, which is an exact
tag-name lookup surfaced on a different endpoint (`GET /api/pointofinterest/filter`).
A user who wants "beach photos in Cape Town from last summer" has no single query
that can express it.

This spec replaces both paths with Apache SOLR as the sole POI search backend,
kept in sync from RabbitMQ, with a full-rebuild path for recovery.

## Current state (verified findings)

- **Two unrelated search paths, both PostgreSQL.**
  `GET /api/pointofinterest/search?term=` → `SearchAsync` → `ILIKE` on `FormattedAddress`
  (`PointOfInterestController.cs:44-51`, `PointOfInterestService.cs:44-45`).
  `GET /api/pointofinterest/filter?filterString=` → `GetAsync(tagName)` → exact tag
  lookup via stored proc (`PointOfInterestController.cs:35-42`, `PointOfInterestService.cs:41-42`).
- **MCP exposes only the address path.** `search_pointofinterest`
  (`mytravels.mcp/Tools/PointOfInterestMcpTools.cs:21-31`) calls the same
  `SearchAsync`, and its `ToDto` (`:33-61`) drops `Description` entirely - an MCP
  client cannot see descriptions even where they exist.
- **`Description` is persisted.** `PointOfInterest.Description`
  (`mytravels.contract/Entities/PointOfInterest.cs:32`) is written by the
  `AppendImageTags` subscriber (`mytravels.messaging/AppendImageTags.cs:53-57`).
  `SPEC.md` §7.5 still describes this as a non-persisted on-demand feature; that
  section is stale and should be corrected as part of this work.
- **The read model already carries everything SOLR needs.**
  `spGetPointOfInterest()` (`Features/PointOfInterest/public.spGetPointOfInterests.sql`)
  returns `Description`, `FormattedAddress`, `DateTaken`, `DateCreated`,
  `PointOfInterestKey` and one row per tag (`TagId`/`TagName`), already deduplicated
  to the newest row per `PointOfInterestKey`. `GetPointOfInterestResponse`
  (`mytravels.contract/Responses/GetPointOfInterestResponse.cs`) mirrors it field for field.
- **Replacing a photo inserts a new row, it does not update one.**
  `AddImageToPointOfInterestAsync` (`CoreDbContext.cs:138-147`) sets `point.Id = 0`
  and calls `AddObject`, so the same `PointOfInterestKey` accumulates rows and only
  the newest is visible through the stored proc. This dictates the SOLR document key
  (see Design decisions).
- **The messaging infrastructure needed here already exists.**
  `MessageSubscriberBase<T>` (`mytravels.common/Services/MessageSubscriberBase.cs`)
  gives fanout declare, bind, prefetch 10, W3C `traceparent` propagation, 3-attempt
  retry with `x-retry-count`, dead-letter to a `-failed` exchange, and audit logging
  keyed on `CorrelationId`. A new subscriber is a constructor call plus one override.
- **The web app does not use POI search.** `src/web/src/api/client.ts` calls only
  `searchPlaces` (geocoding lookup). No frontend change is required.
- **Stage 3 has no Compose file.** Compose stages are 0, 1 and 2 only; stages 3 and 4
  are manifests. `CLAUDE.md` and `SPEC.md` §3 both claim a `3-kubernetes/docker-compose.yml`
  - it does not exist. Correct both while editing them.

## Design decisions

- **SOLR document key is `PointOfInterestKey`, not `PointOfInterestId`.** Because a
  photo replacement inserts a new `Id` under the same key, keying on `Id` would leave
  the superseded row in the index forever, and a full rebuild (which reads the
  latest-per-key stored proc) would silently disagree with the incremental path.
  Keying on the stable key makes both paths converge on the same document set.
  Store `poi_id` as an ordinary field so DTO mapping still returns the right `Id`.
- **The incremental indexer resolves latest-for-key before indexing.** Given a message
  carrying `PointOfInterestId`, the consumer loads that row, reads its
  `PointOfInterestKey`, then indexes the *latest* row for that key. Without this,
  two messages for the same key processed out of order would leave stale data in the
  index. This makes indexing order-independent and idempotent.
- **Index after enrichment, not only at creation.** At `CreatePointOfInterestAsync`
  the address, description and tags are all still empty - they arrive asynchronously.
  `index-solr` is therefore published four times per upload: once on create (so the
  POI is findable by date/coordinates immediately) and once at the end of each of
  `AppendFormattedAddress`, `AppendImageTags` and `ResizeImage`. SOLR upserts by key,
  so repeat indexing is free.
- **Search returns `List<GetPointOfInterestResponse>`, unchanged.** SOLR documents are
  mapped back into the existing row-per-tag response shape, so `IPointOfInterestService`'s
  signature, both `ToDto` helpers, and every caller stay as they are. Blast radius is
  the service implementation, not the API contract.
- **Schema is created at runtime through SOLR's Schema API, not a mounted configset.**
  A configset would have to be maintained as a bind mount in Compose *and* as a
  ConfigMap in stage 3 *and* stage 4 - three copies to drift apart, which is exactly
  the failure mode `CLAUDE.md` warns about. A `SolrSchemaInitializer : IHostedService`
  in `messaging` that idempotently PUTs field definitions keeps one definition in C#
  and makes every stage self-healing. Trade-off: the schema lives in code rather than
  in a visibly Kubernetes-shaped artifact, which is slightly less on-theme for the
  course; revisit if the runbooks want a ConfigMap to point at.
- **`edismax` with query-time field boosts.** `qf=formatted_address^5 tags^3 description^1`.
  Boosts are a query parameter, so tuning relevance needs no reindex and no schema change.
- **Dates are a filter, not free text.** `from`/`to` query parameters map to an `fq`
  range on `date_taken` with `date_created` as the fallback when `date_taken` is null.
  Free-text date parsing ("last summer") is out of scope.
- **No SolrNet dependency.** `mytravels.common` already uses Flurl.Http 4.0.2 for both
  maps services; SOLR's `/select` and `/update` handlers are plain JSON over HTTP.
  Follow the existing convention rather than adding a client library.
- **Delete-sync is out of scope.** No delete endpoint exists on
  `PointOfInterestController`, so there is nothing to hook. If one is added later it
  must publish `index-solr` with a delete flag.

## Changes

### Contract (`mytravels.contract`)

- `Constants/ExchangeNames.cs` - add four constants following the existing
  `X` / `X-failed` pairing:
  `IndexSolr = "index-solr"`, `IndexSolrFailed = "index-solr-failed"`,
  `ReindexSolr = "reindex-solr"`, `ReindexSolrFailed = "reindex-solr-failed"`.
- `Messages/SolrReindexMessage.cs` - new `IMessage` with `CorrelationId` and a
  `PurgeFirst` bool (default `true`). It deliberately has no `PointOfInterestId`;
  `MessageSubscriberBase`'s audit reflection (`MessageSubscriberBase.cs:132-141`)
  reads that property by name and tolerates its absence.
- `Interfaces/ISolrSearchService.cs` - `SearchAsync(SolrSearchQuery query, CancellationToken)`
  returning `List<GetPointOfInterestResponse>`.
- `Interfaces/ISolrIndexService.cs` - `IndexAsync(int pointOfInterestId, CancellationToken)`,
  `IndexBatchAsync(IEnumerable<...>, CancellationToken)`, `PurgeAsync(CancellationToken)`,
  `EnsureSchemaAsync(CancellationToken)`.
- `Dtos/SolrSearchQuery.cs` - `Term`, `Tag`, `From`, `To`, `Rows`, `Start`.
- `Config/SolrConfig.cs` - `Url`, `Collection`, `TimeoutSeconds`, `BatchSize`
  (bound from a `Solr` config section, mirroring `MinIOConfig`).

`PointOfInterestMessage` is reused as-is for `index-solr`.

### Common (`mytravels.common/Services`)

- `SolrClient.cs` - thin Flurl wrapper over `/select`, `/update?commit=true`,
  and `/schema`. Same Polly shape as `GoogleMapsService`: 2 retries, exponential backoff.
- `SolrSearchService.cs` - builds the `edismax` query (`q`, `qf` with the boosts above,
  `fq` for tag and date range, `rows`/`start`), executes it, and expands each returned
  document into one `GetPointOfInterestResponse` per tag - or a single row with null
  `TagId`/`TagName` when the document has no tags, matching what the stored proc
  produces today.
- `SolrIndexService.cs` - implements `ISolrIndexService`. Resolves latest-for-key via
  `ICoreDbContext` (which lives in `contract`, so no new project reference is needed),
  maps to a SOLR document, and posts it. `EnsureSchemaAsync` issues idempotent
  `add-field` calls and ignores "field already exists" responses.

### Domain (`mytravels.domain`)

- `CoreDbContext.cs` - delete `SearchPointsOfInterestByFormattedAddressAsync` (`:44-70`)
  and `GetPointsOfInterestByTagAsync` (`:35-36`); remove both from `ICoreDbContext`.
- `Features/PointOfInterest/public.spGetPointOfInterestByTagName.sql` - delete, and add
  a migration that drops the function. Note the repo convention: stored-proc changes ship
  as EF migrations (`SPEC.md` §16), and `SeedData`/`UpdateStoredProcedure` carry
  deliberately future-dated timestamps so they re-run last - do not disturb those two ids.
- `PointOfInterestService.cs` - `SearchAsync` delegates to `ISolrSearchService`;
  `GetAsync(string tagName)` delegates to the same service with the tag filter set.
  `CreatePointOfInterestAsync` (`:108-133`) gains a fourth publish to
  `ExchangeNames.IndexSolr` alongside the existing three.

### API (`mytravels.api`)

- `Controllers/PointOfInterestController.cs` - `SearchAsync` (`:44-51`) gains optional
  `tag`, `from`, `to`, `rows`, `start` query parameters. `GetMetadatasAsync(filterString)`
  (`:35-42`) keeps its route and shape for compatibility but now resolves through SOLR.
- New `POST /api/pointofinterest/reindex` - mints a `CorrelationId`, publishes a
  `SolrReindexMessage` to `ExchangeNames.ReindexSolr`, returns `202 Accepted` with the
  correlation id so the caller can follow it through the existing traceability UI.
  It must not do the rebuild inline.
- `appsettings.json` - add the `Solr` section. While here, drop the stale
  `AnthropicApiKey` placeholder (`CLAUDE.md` notes `api` does not use it).
- `Program.cs` - register `SolrConfig`, `ISolrSearchService`, `ISolrIndexService`.

### MCP (`mytravels.mcp`)

- `Tools/PointOfInterestMcpTools.cs` - widen `search_pointofinterest` to accept
  `tag`, `from`, `to`; update the `[Description]` text, which currently promises
  address-only search. Add `Description` to the `ToDto` projection (`:40-57`) - it is
  populated in the response object and silently discarded today.
- `Program.cs` and `appsettings.json` - same registrations and `Solr` section as `api`.

### Messaging (`mytravels.messaging`)

- `IndexSolr.cs` - new `MessageSubscriberBase<PointOfInterestMessage>`, constructed with
  `(ExchangeNames.IndexSolr, ExchangeNames.IndexSolr, ExchangeNames.IndexSolrFailed)`,
  following `AppendImageTags.cs` line for line.
- `ReindexSolr.cs` - new `MessageSubscriberBase<SolrReindexMessage>`. Purges the
  collection when `PurgeFirst`, then pages `GetAllPointsOfInterestAsync()` in
  `SolrConfig.BatchSize` chunks, grouping by `PointOfInterestId` to collect tags, and
  posts each batch. Log progress per batch - a full rebuild is the one operation here
  with no natural per-item observability.
- `SolrSchemaInitializer.cs` - `IHostedService` calling `EnsureSchemaAsync` at startup.
  Must tolerate SOLR not yet being reachable (retry with backoff) rather than crashing
  the host, since Compose `depends_on` does not guarantee readiness.
- `AppendFormattedAddress.cs`, `AppendImageTags.cs`, `ResizeImage.cs` - publish
  `index-solr` on successful completion, reusing the inbound `CorrelationId` so the
  reindex is visible on the same trace.
- `FailedExchangeDeclarer.cs` - declare `index-solr-failed` and `reindex-solr-failed`
  alongside the existing three.
- `Program.cs` - register the two subscribers and the schema initializer as hosted
  services, and the SOLR services in DI.

### SOLR schema

Collection `mytravels-pois`. `id` (string, uniqueKey) holds `PointOfInterestKey`.

| Field | Type | Multi | Notes |
|---|---|---|---|
| `id` | string | no | `PointOfInterestKey` |
| `poi_id` | pint | no | `PointOfInterestId`, for DTO mapping |
| `formatted_address` | text_general | no | boost ^5 |
| `tags` | text_general | **yes** | boost ^3 |
| `tag_exact` | string | **yes** | for exact `fq` tag filtering |
| `description` | text_general | no | boost ^1 |
| `date_taken` | pdate | no | nullable |
| `date_created` | pdate | no | |
| `latitude` / `longitude` | pdouble | no | stored, not searched |
| `container`, `original_file_name`, `generated_blob_name` | string | no | stored, needed to rebuild the response |
| `image_resized` | boolean | no | |
| `correlation_id` | string | no | |

`tags` is indexed twice deliberately: tokenised for relevance, and as `tag_exact`
for the filter path that `spGetPointOfInterestByTagName` used to serve.

### Infrastructure

Compose - `0-local/`, `1-dockerize/`, `2-dockerhub/` (stage 0 runs the app from
source but its infra comes from Compose, so SOLR belongs there too):

```yaml
solr:
  image: solr:9.x            # pin an explicit tag; verify it on Docker Hub first
  container_name: mytravels-solr
  ports: ["8983:8983"]
  command: ["solr-precreate", "mytravels-pois"]
  volumes: [solr-data:/var/solr]
  healthcheck:
    test: ["CMD-SHELL", "curl -f http://localhost:8983/solr/mytravels-pois/admin/ping || exit 1"]
    interval: 10s
    timeout: 5s
    retries: 5
```

Add a `solr-data` named volume. `api`, `mcp` and `messaging` gain
`Solr__Url: ${SOLR_URL}` / `Solr__Collection: ${SOLR_COLLECTION}` and a
`depends_on: solr: {condition: service_healthy}`.

Stage 3 (`3-kubernetes/manifests/solr/`, numbered for apply order):
`1-pv-pvc.yaml`, `2-deployment.yaml`, `3-service.yaml`. Follow
`minio/2-pv-pvc.yaml` - note it hard-pins `nodeAffinity` to
`k3d-mytravels-agent-2`; match that so SOLR lands on the same node as the other
`hostPath` volumes. Add `solr.mytravels.local` → `solr:8983` to
`9-ingress.yaml` for the Admin UI, and to the host comment block at the top,
which currently lists every other exposed host.

Stage 4 (`4-argocd/manifests/solr/`): same three files without the number
prefixes, plus `argocd.argoproj.io/sync-wave` annotations placing SOLR in the
same wave as Postgres/RabbitMQ/MinIO, ahead of `api`/`messaging`/`mcp`. Stage 4
keeps no Secret manifests - nothing here is secret, so no `.env` plumbing is needed
beyond the two `Solr__*` values.

`.env.example` for stages 0, 1, 2 and 4: add `SOLR_URL`, `SOLR_COLLECTION`.

Image tags: `api`, `mcp` and `messaging` all change, so all three need a bump in
`2-dockerhub/docker-compose.build.yml` (the authoritative build file) and then in
every Compose file and manifest that pins them. Current tags are
`api:v1.0.10`, `messaging:v1.0.13`, `mcp:v1.0.1`. Build and push before touching
stages 2–4, or those stages break on pull.

### Documentation

- `SPEC.md` - §6.1/§6.2 (new and changed endpoints and the MCP tool signature),
  §6.4 (two new exchange pairs), §8.3 (dropped stored proc), §9 (SOLR as a second
  persistence store), §10 (the `Solr` config section), §14 (SOLR as an integration).
  Also fix §7.5, which wrongly says descriptions are not persisted, and §3, which
  lists a `3-kubernetes/docker-compose.yml` that does not exist.
- `CLAUDE.md` - add SOLR to the architecture sketch and to the "a service added to
  the app must be wired into all five stages" checklist; correct the same phantom
  stage-3 Compose file.
- All five `runbook.ipynb` - a SOLR section per stage: bring it up, confirm the
  collection exists, run a search, trigger a reindex. Per the repo's runbook rule
  (`.claude/skills/validate-runbook/`), a failed health check must run its diagnostic
  command inline in the same cell, never print a suggestion to run one. Validate each
  notebook end-to-end with the `validate-runbook` skill afterwards.
- `drawio/architecture.drawio` - add SOLR and the two new exchanges.

## Implementation order

1. Contract: constants, message, interfaces, config, DTO. Nothing depends on
   anything yet, and it unblocks everything else.
2. `SolrClient` + `SolrSchemaInitializer`, then stand SOLR up in `1-dockerize` only
   and confirm the schema applies against a real instance. Get this right before
   replicating the Compose service into stages 0 and 2.
3. `SolrIndexService` and the `IndexSolr` subscriber, plus the publish calls in
   `PointOfInterestService` and the three enrichment subscribers. Upload a photo and
   watch the document appear in the Admin UI.
4. `ReindexSolr` subscriber and the `POST /reindex` endpoint. Verify purge-and-rebuild
   converges on the same document set the incremental path produced in step 3 - that
   equivalence is the whole point of the key design decision above, so test it
   explicitly rather than assuming it.
5. `SolrSearchService`, then repoint `PointOfInterestService.SearchAsync` and
   `GetAsync(tagName)`. Confirm the existing REST and MCP responses are byte-identical
   in shape to what PostgreSQL returned.
6. Delete the PostgreSQL search paths and the stored proc, with its drop migration.
7. Replicate infrastructure into stages 0, 2, 3, 4. Bump and push image tags.
8. Documentation, runbooks, diagram.

## Out of scope

- SolrCloud / multi-replica SOLR. Single instance with a PVC; a rebuild from
  PostgreSQL is the recovery path, which is why step 4 exists.
- Authentication on SOLR. It is unauthenticated like every other service in this
  stack (`SPEC.md` §11) and exposed on an ingress host for the same reason the
  RabbitMQ and MinIO consoles are.
- SOLR's own Prometheus exporter. The app's calls to SOLR are already traced through
  `AddHttpClientInstrumentation`; a `solr-exporter` sidecar can follow later if the
  Grafana dashboards want index-level metrics.
- Free-text date parsing, geospatial radius search, autocomplete, faceting.
- Frontend work. The web app does not call POI search today.
- Delete propagation, until a delete endpoint exists.
