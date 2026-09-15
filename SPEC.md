# MyTravels / intro-to-k8s — Technical Specification

**Repository:** `k8sforum/intro-to-k8s`
**Generated:** 2026-08-19, reverse-engineered from source (`devops/` excluded per instruction — none exists in this repo).
**Revised:** 2026-09-13 — message traceability (correlation IDs, `MessageAuditLogs`, bounded retry + failed exchanges), async image description/tagging, POI search endpoint. Full revision log at the end.

This document specifies two things that are inseparable in this repo:

1. **MyTravels** — a .NET 10 / React geolocation Points-of-Interest (POI) application (source in `src/`).
2. **A five-stage Kubernetes deployment tutorial** that deploys the same application with progressively more sophisticated tooling (`0-local/` → `4-argocd/`), each stage a self-contained lesson with its own `docker-compose.yml`/manifests, `.env.example`, and a Jupyter `runbook.ipynb` that walks through it hands-on.

Throughout, **Observed:** marks behavior read directly from code/config; **Inferred:** marks reasonable interpretation not directly stated.

---

## 1. Overview

**MyTravels** lets a user upload a geotagged photo (or pick a location manually) to create a "Point of Interest" pin on a map. GPS coordinates are read from the photo's EXIF data on upload; if absent, the upload is rejected and the user searches for and picks a place instead, posting to a different endpoint. A background worker then asynchronously (a) resizes the uploaded image into a thumbnail, (b) resolves a human-readable address for the coordinates via a maps geocoding API, (c) generates a description and scene tags for the photo via Claude (Anthropic), persisting all three onto the POI, and (d) indexes the result into Apache SOLR, which is the sole backend for POI search. The frontend is a two-route SPA: a Leaflet map view that polls for updates while async resolution completes, and a traceability view that replays the message lifecycle of each upload by correlation ID.

**Top-level components:**

| Component | Role | Source |
|---|---|---|
| `mytravels.api` | REST API — CRUD for POIs, image upload, place search, POI search, message traceability | `src/api/mytravels.api/` |
| `mytravels.mcp` | Model Context Protocol tool server — exposes place and POI search as read-only MCP tools for LLM clients | `src/api/mytravels.mcp/` |
| `mytravels.messaging` | Background worker — image resize, address resolution, AI description/tagging, SOLR indexing and rebuild, retry sweep | `src/messaging/mytravels.messaging/` |
| `mytravels.migration` | One-shot EF Core migration bundle runner | `src/common/mytravels.migration/` |
| `web` | React SPA — map UI, upload flow, traceability UI | `src/web/` |
| `mytravels.common` / `mytravels.contract` / `mytravels.domain` / `mytravels.storage` | Shared libraries (DTOs, entities, EF Core context, geo/maps services, object storage, messaging + audit logging, Anthropic integration, SOLR search + indexing) | `src/common/` |

An **observability stack** (OTel Collector → Prometheus/Tempo → Grafana, plus postgres-exporter and cAdvisor) runs alongside the app in stages 1, 3, and 4. It is infrastructure rather than a MyTravels component, so it is specified separately in §4 and §14 rather than given a row above.

**Runtime topology** (Observed, from `1-dockerize/docker-compose.yml` and `3-kubernetes/manifests/`):

```
                    ┌──────────────┐          ┌──────────────┐
   browser ───────► │  web (nginx) │          │ MCP client   │
                    └──────┬───────┘          │ (LLM host)   │
                           │ REST             └──────┬───────┘
                           │ (substituted URL)       │ MCP over streamable HTTP
                           ▼                         ▼
                    ┌──────────────┐        ┌──────────────┐
                    │ api (5101)   │        │  mcp (5103)  │
                    └──────┬───────┘        └──────┬───────┘
                           │                       │
                           │  both use IPointOfInterestService / IMapsService
                           │                       │
                           ▼                       ▼
                    ┌──────────────┐        ┌───────────────┐
                    │  RabbitMQ    │◄───────│  PostgreSQL   │
                    │ (5672/15672) │        │  (5432)       │
                    └──────┬───────┘        └───────▲───────┘
                           │ consume                │ POIs + MessageAuditLogs
                           ▼                        │
                    ┌──────────────┐                │
                    │ messaging    │────────────────┘
                    │ (5102)       │
                    └──┬──────┬────┴──┐
                       │      │       │ index / reindex
                       ▼      ▼       ▼
            ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
            │  MinIO (S3)  │ │  Anthropic   │ │  SOLR (8983) │
            │ (9000/9090)  │ │ (image desc) │ │ mytravels-   │
            └──────────────┘ └──────────────┘ │ pois         │
                                              └──────▲───────┘
                                                     │ search / tag filter
                                          api ───────┴─────── mcp

   Anthropic is reached only from `messaging` (the AppendImageTags subscriber);
   `api` and `mcp` DI-register IImageDescriptionService but never call it.
   SOLR is written only by `messaging` and read only by `api` and `mcp`; it is a
   derived store rebuildable from PostgreSQL, never a system of record (§9).

   Flagsmith (8000) is evaluated by `api` (enable-poi-search, enable-message-tracing)
   and `messaging` (enable-image-description) via the OpenFeature .NET SDK, and by
   `web` via the OpenFeature React/web SDK — not by `mcp`. It shares the same
   PostgreSQL instance in a second database (FeatureDb), not a second container.

   api, mcp, messaging ──OTLP──► otel-collector ──► Prometheus (metrics) / Tempo (traces) ──► Grafana
   web (browser RUM)   ──OTLP/HTTP──┘                                    (stages 1, 3, 4 only)
```

Five deployment stages progressively wrap this same topology:

| Stage | Directory | Adds |
|---|---|---|
| 0 | `0-local/` | Infra (Postgres/RabbitMQ/MinIO/SOLR) via Compose; app run from source (`dotnet run` / `npm run dev`) |
| 1 | `1-dockerize/` | Full stack containerized, built locally via Compose; adds the full observability stack |
| 2 | `2-dockerhub/` | Images built & pushed to Docker Hub, stack runs from registry images (observability dropped) |
| 3 | `3-kubernetes/` | Deployed to a k3d Kubernetes cluster via raw `kubectl apply` manifests + Traefik ingress |
| 4 | `4-argocd/` | Same manifests, GitOps-deployed via Argo CD (Application/AppProject, sync waves, drift/self-heal) |

**Inferred:** the repo's primary purpose is pedagogical (CKAD/CKA-oriented — see `CERTIFICATION.md`), using a realistic multi-service app as the running example rather than toy manifests.

---

## 2. Architecture & Component Relationships

**Dependency graph** (Observed, `src/mytravels.sln` solution folders + `ProjectReference`s):

```
mytravels.api ────────────────┐
mytravels.messaging ──────────┤
mytravels.mcp ────────────────┼──► mytravels.common ──► mytravels.contract
                               ├──► mytravels.domain ──► mytravels.contract
                               └──► mytravels.storage ─► mytravels.contract
mytravels.migration ───────────────► mytravels.domain (design-time only)
web (React, separate npm project, no dependency on any C# project)
```

**Observed:** `mytravels.mcp` has exactly the same `ProjectReference` set as `mytravels.api` (common, contract, domain, storage) and registers the same DI graph in `Program.cs` — `IPointOfInterestService`, `IMapsService`, `IObjectStorageService`, `IMessagePublisher`, `ICoreDbContext`. It is a **second front end onto the same domain layer**, not a client of the REST API: `PlaceMcpTools` calls `IMapsService.SearchPlaceAsync` directly, and `PointOfInterestMcpTools` calls `IPointOfInterestService.SearchAsync` for POI search, which now resolves through SOLR.

Consequence: a behaviour change in POI **search** or geocoding lands on both entry points at once. `mcp`'s registration is a near-copy of `api`'s and still includes `IMessagePublisher`, `IMessageAuditLogger`, `IGeoService` and `IImageDescriptionService` even though no surviving MCP tool reaches any of them — they are there to satisfy `PointOfInterestService`'s constructor, not because `mcp` writes anything.

The two surfaces are **not feature-equivalent**: the REST API exposes CRUD operations (list, fetch, search, update, upload) plus the traceability read surface, none of which have an MCP counterpart. The MCP server is read-only search: `search_pointofinterest` (SOLR-backed free-text search over saved POIs' address, tags and description) and `search_place` (place lookup) — an MCP client can only query existing data, not create or modify anything. The earlier `upload_photo` / `upload_photo_with_coordinates` tools were removed, which also retired the validation-drift and error-handling findings that applied to them (former F-16 and F-17).

**Message flow** (Observed, `src/common/mytravels.contract/Constants/ExchangeNames.cs`, `MessageSubscriberBase.cs`, consumer files):

```
PointOfInterestService.CreatePointOfInterestAsync
        │  (mints CorrelationId, stamps it on the row and on all three messages)
        │
        ├──publish──► fanout "append-formatted-address" ──► queue "append-formatted-address"
        │                                                              │
        │                                                   AppendFormattedAddress (HostedService)
        │                                                              │
        │                                                   IMapsService.GetAddressAsync (Google or OSM)
        │                                                              │
        │                                                   UPDATE PointOfInterests.FormattedAddress
        │
        ├──publish──► fanout "resize-image" ──► queue "resize-image"
        │                                                   │
        │                                          ResizeImage (HostedService)
        │                                                   │
        │                                          MinIO: uploaded-images ─► resize 10% ─► resized-images
        │                                                   │
        │                                          UPDATE PointOfInterests.ImageResized = true
        │                                                   │
        │                                                   └──publish──► fanout "append-image-tags"
        │                                                                        │
        │                                                               AppendImageTags (HostedService)
        │                                                                        │
        │                                                      IImageDescriptionService.DescribeAsync (Claude)
        │                                                                        │
        │                                                      UPDATE PointOfInterests.Description
        │                                                      + spUpdatePointOfInterestTags(tags)
        │                                                                        │
        │                                                                        └──publish──► "index-solr"
        │
        └──publish──► fanout "index-solr" ──► queue "index-solr"
                                                            │
                                                   IndexSolr (HostedService)
                                                            │
                                                   resolve latest row for PointOfInterestKey
                                                            │
                                                   POST SOLR /update?commit=true  (upsert by key)

"append-image-tags" is NOT published at creation - it is chained off the tail of ResizeImage, so
the description and tags are generated exactly once per image and the "index-solr" message that
carries them is only published once DescribeAsync has succeeded and both fields are persisted.

The enrichment subscribers each re-publish "index-solr" on success, reusing the inbound
CorrelationId - the address, description and tags are all empty at creation time, so the POI is
indexed once up front and then re-indexed as each field lands. SOLR upserts by key, so the
repeat is free.

A full rebuild is a separate exchange, published only by POST /api/pointofinterest/reindex:

        fanout "reindex-solr" ──► queue "reindex-solr" ──► ReindexSolr (HostedService)
                                                                  │
                                                     optional purge, then page spGetPointOfInterest()
                                                     in Solr:BatchSize chunks and POST each batch

Every publish and every consume outcome writes a MessageAuditLogs row (best-effort):
        MessagePublisher ──► "Published"
        MessageSubscriberBase<T> ──► "ConsumeSucceeded" | "Retried" | "Failed"

On consume failure, MessageSubscriberBase<T> republishes to the SAME exchange with an
incremented x-retry-count header; after 3 retries it publishes a FailedMessage to the
paired "<exchange>-failed" fanout exchange and acks:
        resize-image-failed | append-formatted-address-failed | append-image-tags-failed
        index-solr-failed   | reindex-solr-failed

AppendFormattedAddressSweeper (CronJobBase, every 30 min)
        └── full-table scan PointOfInterests, retry rows created in last 2 days with empty FormattedAddress
```

All ten exchanges are RabbitMQ **fanout**, bound with an empty routing key. The five work exchanges are declared independently by both the publisher and the consumer (no shared topology-setup step); the five `-failed` exchanges are declared `durable: false, autoDelete: true` both by `FailedExchangeDeclarer` (an `IHostedService` in `messaging` that runs once at startup) and again by each `MessageSubscriberBase<T>` — see §17 for what this duplication implies at scale. **Nothing consumes the `-failed` exchanges**: they are fanout with no bound queue, so a dead-lettered `FailedMessage` is dropped on publish. The durable record of the failure is the `MessageAuditLogs` `Failed` row, not the message.

**Request flow (API)**: browser → `web` (nginx, static SPA) → directly to `api`'s public URL (no reverse proxy through `web`) → `CoreDbContext` (EF Core / Npgsql) → PostgreSQL. The URL is a build-time sentinel substituted at deploy time in stages 2–4 and a literal build arg in stage 1 (§10.5). Image bytes are stored in MinIO, not the database; the DB holds only the blob name/container reference.

---

## 3. Repository & Folder Structure

```
intro-to-k8s/
├── 0-local/                     # Stage 0: infra via Compose, app run from source
│   ├── docker-compose.yml       # postgres, rabbitmq, minio, solr, migration job
│   ├── runbook.ipynb            # hands-on lesson notebook
│   └── scripts/migrations.ps1
├── 1-dockerize/                 # Stage 1: full stack containerized (local build)
│   ├── docker-compose.yml
│   ├── observability/           # otel-collector, prometheus, tempo configs + Grafana provisioning/dashboards
│   ├── tools/                   # stray dir: only a gitignored node_modules/, no tracked source
│   └── runbook.ipynb
├── 2-dockerhub/                 # Stage 2: images built & pushed to Docker Hub
│   ├── docker-compose.yml       # pulls from registry; no observability stack
│   ├── docker-compose.build.yml # multi-arch build/push helper — the authoritative image tags
│   └── runbook.ipynb
├── 3-kubernetes/                # Stage 3: raw kubectl-applied manifests + Traefik ingress
│   ├── manifests/               # 1-namespace.yaml, 8-traefik-config.yaml, 9-ingress.yaml, plus
│   │                            #   api/, mcp/, messaging/, migrations/, minio/, observability/,
│   │                            #   postgres/, rabbitmq/, solr/, web/ — files numbered for apply order
│   ├── roadmap.md
│   └── runbook.ipynb            # NOTE: no docker-compose.yml — Compose stops at stage 2
├── 4-argocd/                    # Stage 4: GitOps deploy of the stage-3 manifests via Argo CD
│   ├── argocd/                  # Application, AppProject, accounts, ingress, server-params
│   ├── cluster/traefik-config.yaml
│   ├── manifests/                # same shape as 3-kubernetes/manifests, secrets removed,
│   │                             #   filename number prefixes dropped, sync-wave annotations added
│   └── runbook.ipynb
├── src/                          # Application source (the actual MyTravels app)
│   ├── api/mytravels.api/        # ASP.NET Core REST API (net10.0)
│   ├── api/mytravels.mcp/        # MCP tool server (net10.0, Web SDK) — Tools/, Dockerfile, appsettings
│   ├── messaging/mytravels.messaging/  # Background worker (net10.0, Web SDK)
│   ├── common/
│   │   ├── mytravels.common/     # Shared services: geo, maps, RabbitMQ pub/sub + audit logging,
│   │   │                         #   cron base classes, Anthropic image description
│   │   ├── mytravels.contract/   # Entities, DTOs, interfaces, constants, messages, custom exceptions
│   │   ├── mytravels.domain/     # EF Core DbContext, migrations, stored-proc SQL,
│   │   │                         #   Features/PointOfInterest/ and Features/Traceability/
│   │   ├── mytravels.storage/    # MinIO / Azure Blob storage adapters
│   │   └── mytravels.migration/  # EF Core migrations-bundle build project
│   ├── web/                      # React 19 + Vite + TypeScript + Tailwind v4 SPA (react-router, 2 routes)
│   └── scripts/                  # DB grant script, multi-arch manifest merge script
├── scripts/init-dbs.sql          # Postgres first-run hook (currently a no-op)
├── drawio/architecture.drawio    # Architecture diagram source
├── prompts/                      # Scaffolding + feature prompts kept as a record of how features were
│   └── todo/                     #   specced ("scaffold api mcp.md", "async image description design
│                                 #   prompt.md", "find traceability issues.md", "implement search.md"
│                                 #   — the SOLR spec, now implemented, see §9); todo/ still holds
│                                 #   "traceability gaps spec.md", not yet implemented
├── user stories/topic rating.md  # Course-content notes, not application requirements
├── .claude/                      # commands/upload-photos.md, skills/validate-runbook/, settings.json
├── CERTIFICATION.md              # CNCF/Linux Foundation cert-path reference notes
├── AGENTS.md                     # Agent instruction: never refactor bin/obj/Migrations
├── CLAUDE.md                     # Working notes for Claude Code (stage layout, cross-stage gotchas)
└── 0-vs code extensions.md, 1-install tools (*).md, 3-install certificates.md, 9-compare k8s tools.md
                                   # Standalone setup/reference docs for the tutorial series
```

Excluded from the tree above (per instructions/format norms): `node_modules/`, `bin/`, `obj/`, `dist/`, `.git/`, `.worktrees/`, `.run/` (compose log capture), IDE folders (`.vscode/`), and each stage's gitignored `.env`.

---

## 4. Technology Stack

| Component | Version | Source |
|---|---|---|
| .NET SDK / runtime | 10.0 (Alpine base images; `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` for api, `10.0` glibc for messaging) | `src/api/mytravels.api/mytravels.api.csproj:TargetFramework`, `src/api/mytravels.api/Dockerfile`, `src/messaging/mytravels.messaging/Dockerfile` |
| ASP.NET Core | 10.0 (implicit, via `Sdk.Web`) | `src/api/mytravels.api/mytravels.api.csproj` |
| Entity Framework Core | 9.0.9 | `src/api/mytravels.api/mytravels.api.csproj:13`, `src/common/mytravels.domain/mytravels.domain.csproj` — **note: EF Core 9.x pinned under a net10.0 TFM**, see Finding F-9 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 | `src/api/mytravels.api/mytravels.api.csproj` |
| PostgreSQL | 17.6 (Alpine image) | `1-dockerize/docker-compose.yml` (postgres service), `3-kubernetes/manifests/postgres/3-deployment.yaml` |
| RabbitMQ | 3-management (Docker tag) / RabbitMQ.Client 7.1.2 (.NET SDK) | `1-dockerize/docker-compose.yml`, `src/common/mytravels.common/mytravels.common.csproj` |
| MinIO | `quay.io/minio/minio` (latest tag, unpinned) / Minio SDK 6.0.5 | `1-dockerize/docker-compose.yml`, `src/common/mytravels.storage/mytravels.storage.csproj` |
| Apache SOLR | 9.10.1 (pinned) — **no .NET client library**: `SolrClient` talks to `/select`, `/update` and `/schema` over Flurl, following the convention the maps services set | `0-local/`, `1-dockerize/`, `2-dockerhub/docker-compose.yml`, `3-kubernetes/manifests/solr/2-deployment.yaml`, `4-argocd/manifests/solr/deployment.yaml` |
| Azure.Storage.Blobs | 12.24.0 (present but unused — see Finding F-1) | `src/common/mytravels.storage/mytravels.storage.csproj` |
| Anthropic (.NET SDK) | 12.44.0 | `src/common/mytravels.common/mytravels.common.csproj` — used by `AnthropicImageDescriptionService` for Claude Haiku image description |
| Magick.NET-Q16-AnyCPU | 14.10.0 | `src/messaging/mytravels.messaging/mytravels.messaging.csproj` |
| Flurl.Http | 4.0.2 | `src/common/mytravels.common/mytravels.common.csproj` |
| Polly | 8.5.2 | `src/common/mytravels.common/mytravels.common.csproj` |
| ModelContextProtocol / ModelContextProtocol.AspNetCore | 2.2.0 | `src/api/mytravels.mcp/mytravels.mcp.csproj` |
| OpenTelemetry (Exporter.OpenTelemetryProtocol, Extensions.Hosting, Instrumentation.AspNetCore / .Http / .Runtime) | 1.17.0 | `src/api/mytravels.api/`, `src/api/mytravels.mcp/`, `src/messaging/mytravels.messaging/` `.csproj` |
| `@opentelemetry/*` (browser RUM: sdk-trace-web, instrumentation-fetch, instrumentation-document-load, exporter-trace-otlp-http, context-zone) | 2.10.x / 0.221.x | `src/web/package.json:13-22` |
| OpenTelemetry Collector (contrib) | 0.116.1 | `1-dockerize/docker-compose.yml`, `*/manifests/observability/*otel-collector-deployment.yaml` |
| Prometheus | v3.1.0 | same |
| Grafana | 11.4.0 | same |
| Grafana Tempo | 2.6.1 | same |
| postgres-exporter | v0.15.0 | same |
| cAdvisor | v0.49.1 | Compose service (stage 1); DaemonSet in stages 3–4 |
| Swashbuckle.AspNetCore (Swagger) | 9.0.5 | `src/api/mytravels.api/mytravels.api.csproj` |
| Newtonsoft.Json | 13.0.4 | `src/api/mytravels.api/mytravels.api.csproj`, `mytravels.common.csproj` |
| MetadataExtractor (EXIF) | 2.8.1 | `src/common/mytravels.contract/mytravels.contract.csproj`, `mytravels.common.csproj` |
| Geolocation | 1.2.1 | `src/common/mytravels.domain/mytravels.domain.csproj` |
| System.Security.Cryptography.Xml | 10.0.10 (outlier vs. 9.x elsewhere) | `src/common/mytravels.domain/mytravels.domain.csproj:40` |
| React | 19.2.8 | `src/web/package-lock.json` |
| React DOM | 19.2.8 | `src/web/package-lock.json` |
| react-leaflet | 5.0.0 | `src/web/package-lock.json` |
| react-router-dom | 7.18.3 — added with the traceability UI; the SPA has two routes (`/`, `/traceability`) | `src/web/package.json`, `src/web/src/App.tsx` |
| leaflet | 1.9.4 | `src/web/package-lock.json` |
| Vite | 8.2.0 | `src/web/package-lock.json` |
| TypeScript | 6.0.3 | `src/web/package-lock.json` |
| Tailwind CSS | 4.3.3 (CSS-first `@theme` syntax, no `tailwind.config.js`) | `src/web/package.json`, `src/web/src/index.css` |
| oxlint | 1.76.0 | `src/web/package-lock.json` |
| Node.js (build stage) | 22-alpine | `src/web/Dockerfile` |
| nginx (serve stage) | 1.27-alpine-slim | `src/web/Dockerfile` |
| k3d / Kubernetes | version not pinned in repo (tutorial assumes locally installed toolchain) | `3-kubernetes/runbook.ipynb` prereq steps |
| Traefik | via k3d's bundled Traefik + `HelmChartConfig` patch | `3-kubernetes/manifests/8-traefik-config.yaml`, `4-argocd/cluster/traefik-config.yaml` |
| Argo CD | version not pinned (installed via its own manifests during the runbook) | `4-argocd/runbook.ipynb` |
| dotnet-ef (CLI tool) | 9.0.9 (pinned) | `src/.config/dotnet-tools.json` |

**Version-driven risk (feeds §17):** every first-party .NET project targets `net10.0` but pins Microsoft.EntityFrameworkCore*, Microsoft.Extensions.*, and Npgsql packages to the `9.0.x`/`9.0.4` line — a consistent one-major-version skew across the whole solution (not obviously broken, but worth confirming intentional before treating `net10.0` as fully adopted). MinIO's Docker image is pulled with no version tag (`quay.io/minio/minio`, unpinned) in every Compose file — a reproducibility risk since "latest" drifts over time.

---

## 5. Module & Service Responsibilities

| Module | Purpose | Owned domain | Key types | Depends on |
|---|---|---|---|---|
| `mytravels.api` | Public REST surface | HTTP request/response, Swagger docs, CORS, global exception mapping | `PointOfInterestController`, `PlaceController`, `TraceabilityController`, `ApiExceptionMiddleware` | common, contract, domain, storage |
| `mytravels.mcp` | MCP tool surface for LLM clients | Tool schema/descriptions, tool-level argument validation; read-only (search) | `PointOfInterestMcpTools`, `PlaceMcpTools` | common, contract, domain, storage |
| `mytravels.messaging` | Async processing | Image resize, address resolution, AI description/tagging, SOLR indexing and full rebuild, SOLR schema creation, retry sweep, failed-exchange topology | `ResizeImage`, `AppendFormattedAddress`, `AppendImageTags`, `IndexSolr`, `ReindexSolr`, `SolrSchemaInitializer`, `AppendFormattedAddressSweeper`, `FailedExchangeDeclarer` (all `IHostedService`/`BackgroundService`) | common, contract, domain, storage |
| `mytravels.migration` | Schema management | Produces `efbundle` — a self-contained EF Core migration-apply executable; not itself a migration runner at execution time (see Finding F-3) | `CoreDbContextFactory` (design-time only) | domain |
| `mytravels.common` | Cross-cutting services | Geocoding (Google/OSM), RabbitMQ publish/subscribe base classes (incl. retry, dead-lettering, trace propagation and audit logging), cron scheduling base class, AI image description, SOLR search and indexing | `GoogleMapsService`, `OpenStreetMapsService`, `ImageMetadataService`, `AnthropicImageDescriptionService`, `MessagePublisher`, `MessageSubscriberBase<T>`, `CronJobBase`, `SolrClient`, `SolrSearchService`, `SolrIndexService` | contract |
| `mytravels.contract` | Shared data shapes | Entities, DTOs, interfaces, exceptions, constants, config POCOs, message payloads | `PointOfInterest`, `Tag`, `MessageAuditLog`, `PointOfInterestMessage`, `SolrReindexMessage`, `FailedMessage`, `*Dto`, `SolrSearchQuery`, `SolrConfig`, `ICoreDbContext`, `IObjectStorageService`, `IMapsService`, `ITraceabilityService`, `IMessageAuditLogger`, `ISolrSearchService`, `ISolrIndexService` | none (leaf) |
| `mytravels.domain` | Persistence | EF Core DbContext, migrations, stored-proc invocation, POI business logic (search delegated to SOLR), traceability queries | `CoreDbContext`, `PointOfInterestService`, `TraceabilityService`, `MessageAuditLogger` | contract, common |
| `mytravels.storage` | Object storage abstraction | Blob/object read-write | `MinIOStorageService` (active), `AzureStorageService` (dead code, unregistered) | contract |
| `web` | User interface | Map rendering, upload flow, place search, message-traceability timeline | `App.tsx` (routing), `MapPage`, `MapView`, `UploadButton`, `PoiDialog`, `LocationSearchDialog`, `TraceabilityPage` | none (calls API over HTTP only) |

---

## 6. Public Interfaces & APIs

### 6.1 `mytravels.api` — base path `/api`, no versioning, all endpoints anonymous (no `[Authorize]` anywhere)

| Method | Route | Request | Response | Status codes | Auth |
|---|---|---|---|---|---|
| GET | `/api/pointofinterest?rows=&start=` | optional `rows` (default 100), `start` — **served from SOLR** as a single capped `*:*` query since `api:v1.0.12`, not from PostgreSQL | `List<PointOfInterestDto>` | 200 | none |
| GET | `/api/pointofinterest/search?term=&rows=&start=` | query `term` (free text over address, tags and description), optional `rows` (default 100), `start` — **gated behind the `enable-poi-search` Flagsmith flag** (`api:v1.0.13`+), evaluated first via the registered `FeatureClient`; returns `404` before touching `IPointOfInterestService` when off (default `true`) | `List<PointOfInterestDto>` | 200; 404 when `enable-poi-search` is off | none |
| POST | `/api/pointofinterest/reindex?purgeFirst=` | query `purgeFirst` (default `true` when omitted) | `SolrReindexResponseDto { CorrelationId }` | 202 Accepted | none |
| GET | `/api/pointofinterest/{id:int}?resizedImage=bool` | route `id`; query `resizedImage` is accepted but never used (Finding F-2) | `string` (base64 image) | 200 | none |
| PUT | `/api/pointofinterest` | multipart: `image` (file), query `pointOfInterestKey` | `SaveEntityResponseDto` | 200; 403 if image/key missing | none |
| POST | `/api/pointofinterest/image` | multipart: `image` (file) | `SaveEntityResponseDto` | 200; 403 if image missing; **500 if the image carries no GPS EXIF** (`InvalidOperationException("Image is not geocoded")`) | none |
| POST | `/api/pointofinterest/image/coordinates` | multipart: `image` (file) + `coordinates` (`SaveCoordinatesDto`: Latitude/Longitude `[Required][Range]`, FormattedAddress) | `SaveEntityResponseDto` | 200; 403 if image missing; 400 if `ModelState` invalid | none |
| GET | `/api/place?query=&limit=` | query `query` (required, non-blank), `limit` | `List<PlaceDto>` | 200; 403 if `query` blank | none |
| GET | `/api/traceability?page=&pageSize=` | query `page`, `pageSize` (both coerced to 1 / 25 when ≤ 0) — **gated behind `enable-message-tracing`** (`api:v1.0.13`+), same pattern as `/search`: `404` before calling `ITraceabilityService` when off (default `true`) | `List<CorrelationSummaryDto>` | 200; 404 when `enable-message-tracing` is off | none |
| GET | `/api/traceability/{correlationId:guid}` | route `correlationId` — same `enable-message-tracing` gate | `List<MessageAuditLogDto>` (ordered by `CreatedAt`) | 200 (empty list for an unknown id, not 404); 404 when `enable-message-tracing` is off | none |

**`enable-message-tracing` gates reads only.** `MessageAuditLogger`'s writes (`Published`/`ConsumeSucceeded`/`Retried`/`Failed` rows, §7.6) are unaffected by the flag — turning it off hides the UI and 404s these two endpoints without stopping audit collection, so re-enabling it immediately shows a complete history rather than a gap.

**Added (`api:v1.0.11`):** `POST /api/pointofinterest/reindex` publishes a `SolrReindexMessage` to the `reindex-solr` exchange and returns `202` immediately with the correlation id it used, so the caller can follow the rebuild through `/api/traceability`. It never rebuilds inline — the work happens in `messaging` (§6.4, §9).

**Removed:** `POST /api/pointofinterest/{id}/describe` — the synchronous Claude describe endpoint. Description and tags are now produced asynchronously by the `append-image-tags` subscriber and persisted on the POI (§7.5); clients read them off `PointOfInterestDto.description` / `.tags` instead of calling an endpoint.

Global error shape (`ApiErrorDto`, from `ApiExceptionMiddleware.cs`): `{ Id, HttpStatusCode, Message, Title, Links, Code (unset), Detail (unset) }`. `Id` is the current OpenTelemetry trace id (`Activity.Current?.TraceId`), falling back to a fresh GUID when no activity is in scope — so a client-reported error id joins directly to the trace in Tempo. Exception→status mapping: `RequiredParameterNotFoundException`→403, `OutOfRadiusException`→403, `DataNotFoundException`→404, `ApiException`→its own `.StatusCode`, anything else→500 with the raw exception message serialized into the response body (Finding F-8, information disclosure). Root path `/` is special-cased to return plain text `"API is running..."` (not a structured health check).

Swagger UI is mounted at `/swagger` in **every environment**, not gated to Development (Finding F-4).

### 6.2 `mytravels.mcp` — MCP tool surface (streamable HTTP, port 5103)

Mounted via `app.MapMcp()` after `AddMcpServer().WithHttpTransport().WithTools<PointOfInterestMcpTools>().WithTools<PlaceMcpTools>()`. There is no REST surface and no Swagger; the only plain HTTP route is `GET /health` → `200 "mcp is running..."`, which is what the k8s `livenessProbe` targets. Like the API, **every tool is anonymous** — the MCP server implements no authentication and is exposed through its own ingress host (`mcp.mytravels.local`) in stages 3–4.

| Tool | Arguments | Returns | Validation / errors |
|---|---|---|---|
| `search_pointofinterest` | `term` (note: **not** `query`) — the only argument; `SolrSearchQuery` carries no `Tag`, `From` or `To` | `List<PointOfInterestDto>` | `McpException` when `term` is absent or blank. SOLR-backed since `mcp:v1.0.2`: free text is ranked across formatted address, tags and description. Rows are grouped into one DTO per POI with its tags. |
| `search_place` | `query`, `limit` | `List<PlaceDto>` | `McpException` if `query` is blank; `limit` is coerced to a default of 5 when omitted or non-positive. Searches for places by name or address. |

**Note:** Photo upload via MCP is not implemented; POI creation and upload are REST-only, driven from the `api` service (`POST /api/pointofinterest/image` and `POST /api/pointofinterest/image/coordinates`). The MCP server is read-only. `PointOfInterestMcpTools.ToDto` projects `Description` as of `mcp:v1.0.2` but still omits `CorrelationId`, so MCP search results lack that one field where the REST DTO carries it.

### 6.3 `mytravels.messaging` — HTTP surface

Only a catch-all `MapGet("{**path}", ...)` returning `"Service is running..."` — exists purely so the container has a bindable HTTP port for orchestrator liveness checks; the real work happens via hosted services (§6.4), not HTTP.

### 6.4 Message-queue interfaces (RabbitMQ, ten fanout exchanges; work queues are durable and named after their exchange, with no DLX/TTL/max-length arguments)

**Work exchanges** (durable-by-default declaration, one durable queue each):

| Exchange/Queue | Publisher | Consumer | Payload |
|---|---|---|---|
| `append-formatted-address` | `PointOfInterestService.CreatePointOfInterestAsync` | `AppendFormattedAddress` (HostedService) | `PointOfInterestMessage { CorrelationId, PointOfInterestId }` |
| `resize-image` | `PointOfInterestService.CreatePointOfInterestAsync`, `UpdatePointOfInterestAsync` | `ResizeImage` (HostedService) | `PointOfInterestMessage { CorrelationId, PointOfInterestId }` |
| `append-image-tags` | `ResizeImage` (chained on completion, reusing the inbound `CorrelationId`) — **not** published at POI creation | `AppendImageTags` (HostedService) | `PointOfInterestMessage { CorrelationId, PointOfInterestId }` |
| `index-solr` | `PointOfInterestService.CreatePointOfInterestAsync` **and** `AppendFormattedAddress` / `AppendImageTags` on successful completion (three publishes per upload — see §9) | `IndexSolr` (HostedService) | `PointOfInterestMessage { CorrelationId, PointOfInterestId }` |
| `reindex-solr` | `PointOfInterestController.ReindexAsync` (`POST /api/pointofinterest/reindex`) | `ReindexSolr` (HostedService) | `SolrReindexMessage { CorrelationId, PurgeFirst }` — deliberately carries **no** `PointOfInterestId`; the audit reflection in `MessageSubscriberBase` reads that property by name and tolerates its absence |

**Failed exchanges** (`durable: false, autoDelete: true`, declared by `FailedExchangeDeclarer` at startup and again by each subscriber; **no queue is bound to any of them**, so published failures are discarded):

| Exchange | Publisher | Consumer | Payload |
|---|---|---|---|
| `append-formatted-address-failed` | `MessageSubscriberBase<T>` after 3 retries | none | `FailedMessage { CorrelationId, PointOfInterestId, OriginalExchange, ErrorMessage, FailedAt }` |
| `resize-image-failed` | same | none | same |
| `append-image-tags-failed` | same | none | same |
| `index-solr-failed` | same | none | same |
| `reindex-solr-failed` | same | none | `FailedMessage` with a null `PointOfInterestId` |

**Message properties:** publishes set `DeliveryMode = Persistent`, copy `Activity.Current.Id` into a `traceparent` header (the consumer parses it and starts a linked `ActivityKind.Consumer` span on the `MyTravels.RabbitMQ` `ActivitySource`), and set AMQP `CorrelationId` from the payload's `CorrelationId`. Redeliveries carry an `x-retry-count` header, incremented by the subscriber on each republish.

### 6.5 `ICoreDbContext` (library interface consumed within the .NET solution)

Exposes `DbSet`s (`PointOfInterests`, `PointOfInterestTagAssociations`, `Tags`, `MessageAuditLogs`) plus domain methods: `GetPointsOfInterestAsync`, `GetPointsOfInterestByKeyAsync`, `GetAllPointsOfInterestAsync`, `CreatePointOfInterestAsync`, `UpdatePointOfInterestTagsAsync`, `AddImageToPointOfInterestAsync`, `UpdateAddressAsync`, `GetCorrelationSummariesAsync`, `GetEventsByCorrelationIdAsync`, plus generic `AddObject`/`DeleteObject`/`DetachObject`/`Entry`/`ExecuteSqlInterpolatedAsync`.

**The two PostgreSQL search methods are gone** (`api:v1.0.11`). `SearchPointsOfInterestByFormattedAddressAsync` (the `EF.Functions.ILike` over `FormattedAddress` that hard-coded `TagId`/`TagName` to `null`) and `GetPointsOfInterestByTagAsync` (the `spGetPointOfInterestByTagName` wrapper) were both deleted from `CoreDbContext` and from this interface; SOLR serves both paths now (§9, §14). What remains on `ICoreDbContext` is the *write* side plus the by-key and all-POIs reads — the latter two are also what feeds the SOLR indexer, so a change to `spGetPointOfInterest()`'s output shape changes what gets indexed.

### 6.5b `ISolrSearchService` / `ISolrIndexService`

Both live in `mytravels.contract/Interfaces/` and are implemented in `mytravels.common/Services/` over a shared `SolrClient` (a Flurl wrapper — no SolrNet dependency, following the convention the maps services already set). `ISolrSearchService.SearchAsync(SolrSearchQuery, ct)` returns `List<GetPointOfInterestResponse>` in exactly the row-per-tag shape the stored procedures produce, which is why the `IPointOfInterestService` signature, both `ToDto` helpers and every caller were left alone. `ISolrIndexService` exposes `IndexAsync(pointOfInterestId, ct)`, `IndexBatchAsync(rows, ct)`, `PurgeAsync(ct)` and `EnsureSchemaAsync(ct)`. All three services (`api`, `mcp`, `messaging`) register both, but only `messaging` consumes `ISolrIndexService`.

### 6.5a `ITraceabilityService` / `IMessageAuditLogger`

`ITraceabilityService` (implemented by `domain/Features/Traceability/TraceabilityService`) is a thin pass-through to the two `ICoreDbContext` traceability queries, registered only in `api` and consumed only by `TraceabilityController`. `IMessageAuditLogger` (`MessageAuditLogger`, same folder) is a single `LogAsync(MessageAuditLog, ct)` that adds and saves one row; it is registered in all three services (`api`, `mcp`, `messaging`) because `MessagePublisher` takes it as a constructor dependency.

### 6.6 `IObjectStorageService` (library interface, two implementations — see §5, §17)

`GetBase64Async`, `GetObjectAsync<T>`, `GetStreamAsync`, `ListObjectsAsync`, `ListBucketsAsync`, `ObjectExistsAsync`, `RemoveObjectAsync`, `SaveObjectAsync` (×3 overloads), `SaveBase64StringAsync`.

### 6.7 Web frontend → API calls (`src/web/src/api/client.ts`)

| # | Method | Path | Purpose |
|---|---|---|---|
| 1 | GET | `/api/PointOfInterest` | List POIs (initial load + polling) |
| 2 | GET | `/api/PointOfInterest/{id}?resizedImage={bool}` | Fetch a POI's photo (base64 `text/plain`) |
| 3 | POST | `/api/PointOfInterest/image` | Upload photo with EXIF GPS |
| 4 | POST | `/api/PointOfInterest/image/coordinates` | Upload photo + manually-picked place |
| 5 | GET | `/api/Place?query=` | Place search (debounced 400ms, min 3 chars) |
| 6 | GET | `/api/Traceability?page=&pageSize=` | Correlation summaries for the traceability list (called with `page=1, pageSize=50`) |
| 7 | GET | `/api/Traceability/{correlationId}` | Event timeline for one expanded correlation |

Routing (`src/web/src/App.tsx`, react-router): `/` → `MapPage` (map, upload, polling), `/traceability` → `TraceabilityPage`. A single header link toggles between the two.

---

## 7. Business Rules & Workflows

### 7.1 Upload with GPS EXIF (happy path)

1. User selects a photo in `UploadButton` → `POST /api/pointofinterest/image` (multipart).
2. `PointOfInterestService.SaveFileAsPointOfInsterestAsync` (typo preserved from source, `IPointOfInterestService.cs`) saves the raw bytes to MinIO bucket `uploaded-images` (`BucketNames.NewUploadedImagesContainer`).
3. `ImageMetadataService.ExtractImageMetadata` (via `MetadataExtractor`) reads EXIF GPS tags, then `SaveFileAsPointOfInsterestAsync` guards the result: **if either coordinate is `0` it throws `InvalidOperationException("Image is not geocoded")`** (note the guard is `Latitude == 0 || Longitude == 0`, so a genuine on-axis coordinate is rejected too). The object is already in MinIO at this point and is not cleaned up (orphaned blob, §17).
4. Otherwise a `PointOfInterest` row is inserted, with a freshly minted `CorrelationId` set on the entity before the insert.
5. Three messages are published, all carrying that same `CorrelationId`: `append-formatted-address`, `resize-image`, `index-solr`. Each publish writes a `Published` audit row.
6. `AppendFormattedAddress` consumer resolves and writes `FormattedAddress` (skipped if already non-empty — idempotent).
7. `ResizeImage` consumer resizes the image to 10% of original dimensions, uploads to `resized-images`, sets `ImageResized = true` (the resize itself is skipped if already `true` — idempotent). It then publishes `append-image-tags` with the inbound `CorrelationId`; that publish happens on both paths, so a redelivery against an already-resized row still drives the chain rather than dropping it.
8. `AppendImageTags` consumer, if `enable-image-description` is on (default, Flagsmith-gated as of `messaging:v1.0.16`), sends the original image to Claude and writes `Description` + tags (§7.5); either way it then publishes `index-solr` — so the document is indexed with description/tags when the flag is on, or indexed without them when it's off, but `index-solr` fires unconditionally. **Not idempotent when the flag is on** — it has no "already described" guard, so a redelivery re-calls the Anthropic API.
9. Frontend polls `GET /api/PointOfInterest` every 3s (`POLL_INTERVAL_MS`), up to 10 attempts (`POLL_MAX_ATTEMPTS`), watching for the new POI's coordinates to become non-zero.

### 7.2 Upload without GPS EXIF

1. Same upload UI, but the plain `image` endpoint now **rejects** the photo (step 7.1.3) rather than storing a `(0,0)` row.
2. `UploadButton.tsx`'s two-mode menu prompts the user ("No GPS on this photo? Search for the place instead") to open `LocationSearchDialog`, search via `GET /api/place?query=`, and select a result.
3. `POST /api/pointofinterest/image/coordinates` is called instead, with the chosen `Latitude`/`Longitude`/`FormattedAddress` attached directly. On this path EXIF is read only for `DateTaken`, through `TryGetImageMetadataAsync`, which catches `ImageProcessingException` and returns `null` — so a photo with no readable metadata at all is still accepted. `FormattedAddress` is supplied by the client, so the async geocoding step (still published, per step 7.1.5) is a no-op since `FormattedAddress` is already non-empty.

### 7.3 Address-resolution retry sweep

Every 30 minutes, `AppendFormattedAddressSweeper` scans **all** `PointOfInterest` rows, retries geocoding for any row created within the last 2 days with an exactly-empty (`== ""`) `FormattedAddress`. Rows with `NULL` `FormattedAddress` (a valid DB state, column is nullable) are **not** picked up by this sweep even though the live consumer does treat null as needing resolution (`IsNullOrEmpty(...Trim())`) — see Finding F-11. Rows older than 2 days that never resolved are abandoned permanently by the sweep (still resolvable if the original message is somehow redelivered, but no active retry path exists for it after 2 days).

### 7.4 Tagging

`SaveEntityResponseDto`/`SavePointOfInterestDto` support attaching free-text tags to a POI via `UpdatePointOfInterestTagsAsync`, which serializes the tag list to JSON and calls stored procedure `spUpdatePointOfInterestTags`. Tag names are unique (`Tags.Name` has a unique index). In practice the only caller that supplies tags today is the `AppendImageTags` subscriber (§7.5) — there is no user-facing tagging UI.

### 7.5 AI Image Description & Tagging (asynchronous)

1. `ResizeImage` publishes `append-image-tags` at the end of its own processing (§7.1 step 7) — POI creation does not publish it directly. No user action is involved — there is no "Describe" button and no describe endpoint any more. Because replacing a photo publishes only `resize-image`, the same chain also re-describes and re-indexes a replacement image.
2. `AppendImageTags` (in `messaging`) consumes the message, loads the POI, and returns early without error if `GeneratedBlobName` is blank.
3. **As of `messaging:v1.0.16`, it first evaluates the `enable-image-description` Flagsmith flag** (`FeatureClient.GetBooleanValueAsync("enable-image-description", true)`, default `true`, fails open if Flagsmith is unreachable). When `false`, steps 4–6 below are skipped entirely and control goes straight to step 7's `index-solr` publish — the POI is indexed without a description or tags for this pass, matching how those two fields are already empty at creation.
4. When the flag is on, it fetches the **original** image (bucket `uploaded-images`, not the thumbnail) as base64 and calls `AnthropicImageDescriptionService.DescribeAsync`.
5. That service calls Claude with structured output (a JSON schema pinned to `{ description, tags[] }`, `MaxTokens = 512`), using model `AnthropicModel` (default `claude-haiku-4-5`). The response is a one-sentence description (max ~20 words) plus 3–6 single-word lowercase scene tags.
6. **Both are persisted**: `Description` is written with an explicit `EntityState.Unchanged` + `IsModified` toggle (the context is globally `NoTracking`), then tags go through `UpdatePointOfInterestTagsAsync` → `spUpdatePointOfInterestTags` if the list is non-empty.
7. It then publishes `index-solr` with the inbound `CorrelationId` **unconditionally** — whether or not step 3's flag was on — because `description` and `tags` are two of the three fields SOLR ranks on and both were empty when the POI was first indexed at creation (§9); skipping the publish when the flag is off would leave the POI's other async fields (address, thumbnail) permanently unreflected in this indexing pass.
8. `PoiDialog` renders `description`/`tags` straight off the polled POI payload — gated client-side by the same `enable-image-description` flag (evaluated independently via `@openfeature/react-sdk`'s `useBooleanFlagValue`, so the two sides can briefly disagree after a toggle): when off, the description/tags section is hidden entirely, even for POIs described before the flag was turned off.
   - If Anthropic is unconfigured (key unset or still the `<YOUR_ANTHROPIC_API_KEY>` placeholder), `DescribeAsync` throws `ApiException(503, …)` on every message — which, inside a consumer, means 3 retries then a `Failed` audit row per POI, not a clean skip. Turning `enable-image-description` off is now the actual clean skip for an environment with no Anthropic key.
   - Rate limits (429), refusals (422) and API errors (502) surface the same way: as consumer failures routed through the retry/dead-letter path (§12), visible in the traceability UI rather than to any HTTP caller.

### 7.6 Message traceability

1. `PointOfInterestService` mints one `CorrelationId` per POI, stores it on the row, and stamps it on all three creation messages (`append-formatted-address`, `resize-image`, `index-solr`); every downstream publish reuses the inbound id — `ResizeImage` on the `append-image-tags` it chains, and each enrichment subscriber on the `index-solr` it republishes — so the whole enrichment and indexing chain stays on one timeline (on re-upload via `UpdatePointOfInterestAsync`, the existing `CorrelationId` is reused, or minted if the row predates the column).
2. `MessagePublisher.PublishAsync` writes a `Published` audit row before publishing; `MessageSubscriberBase<T>` writes `ConsumeSucceeded`, `Retried` (with the incremented count and the error message), or `Failed`. Correlation-less messages are skipped — `LogAuditEventAsync` returns early on a null/empty `CorrelationId`.
3. All audit writes are wrapped in their own try/catch: a failure is logged at warning level and never propagates, so audit-store problems cannot break message flow (and conversely, an incomplete timeline is not evidence that a step did not run).
4. `GET /api/traceability` groups `MessageAuditLogs` by `CorrelationId` into summaries (`StartedAt`, `LastEventAt`, `EventCount`, `HasFailure`), ordered by most-recent activity and paged with `Skip`/`Take`.
5. `TraceabilityPage` lists those summaries (page 1, size 50, fetched once on mount — no polling) and lazily fetches the per-correlation event timeline when a row is expanded.

---

## 8. Data Models, Schemas, Validation

### 8.1 Entities (`src/common/mytravels.contract/Entities/`, EF Core, PostgreSQL, schema `public`)

**PointOfInterest** (table `PointOfInterests`)
| Field | Type | Nullable | Constraint |
|---|---|---|---|
| Id | int | no | PK, identity |
| PointOfInterestKey | varchar(40) | no | generated `Guid.NewGuid("N")` at creation |
| Container | varchar(250) | no | storage bucket name |
| OriginalFileName | varchar(250) | no | |
| GeneratedBlobName | varchar(250) | no | |
| Latitude | double | no | `0` when unresolved |
| Longitude | double | no | `0` when unresolved |
| DateCreated | timestamptz | no | default `UtcNow` |
| DateTaken | timestamptz | yes | from EXIF, may be absent |
| FormattedAddress | varchar(300) | yes | resolved async |
| ImageResized | bool | no | default `false` |
| DateUpdated | timestamptz | yes | |
| UpdatedBy | uuid | yes | |
| Reason | varchar(500) | yes | |
| Description | text | yes | unbounded (no `[StringLength]`); written by the `AppendImageTags` subscriber |
| CorrelationId | uuid | yes | minted at creation, reused on re-upload; the join key into `MessageAuditLogs` (no FK) |

Navigation: `PointOfInterestTagAssociations` (1:N).

**Tag** (table `Tags`): `Id` PK, `Name varchar(30)` **unique index**, `DateCreated`.

**PointOfInterestTagAssociation** (join table): `Id` PK, `PointOfInterestId` FK (cascade, indexed, required), `TagId` FK (cascade, indexed, required), `DateCreated`.

**MessageAuditLog** (table `MessageAuditLogs`): `Id` PK, `CorrelationId uuid` (indexed, not a FK — the join key across message lifecycle events), `ExchangeName varchar(100)`, `EventType varchar(30)` (`Published`/`ConsumeSucceeded`/`Retried`/`Failed`), `PointOfInterestId int?` (no FK constraint, generic across message types), `RetryCount int`, `ErrorMessage varchar(500)`, `CreatedAt`. Written from `MessagePublisher.PublishAsync` and `MessageSubscriberBase<T>.ReceivedAsync` (best-effort — a write failure is logged, never blocks publish/consume); read by `ITraceabilityService` / `api/Traceability` for the message-traceability UI (`src/web/src/components/TraceabilityPage.tsx`). Replaces the earlier `PointOfInterestAuditLog`/`PointOfInterestAuditLogs`, which had no writers anywhere in the code.

**GetPointOfInterestResponse** — a denormalized, `ExcludeFromMigrations()` query-only type backing stored-procedure results (`RowId`, `PointOfInterestId`, storage fields, lat/long, dates, `FormattedAddress`, `ImageResized`, `TagId`, `TagName`, `PointOfInterestKey`); columns typed `text` regardless of the source entity's `varchar(n)` bounds.

**Schemas**: `public` (application tables), `config` (`EFMigrationsHistory` table only).

### 8.2 DTOs / client-side validation

| DTO | Validation |
|---|---|
| `SaveCoordinatesDto` | `Latitude`/`Longitude`: `[Required][Range]`; `FormattedAddress`: none |
| `UpdateAddressDto` | `PointOfInterestKey`: `[Required][StringLength(40)]`; `Latitude`/`Longitude`: `[Required][Range]` |
| `PointOfInterestDto` | `FormattedAddress`: `[StringLength(300)]` |
| Others (`CreatePointOfInterestDto`, `PlaceDto`, `TagDto`, `SaveEntityResponseDto`, `SavePointOfInterestDto`) | no data-annotation validation |

**Observed divergence**: server-side validation exists only via data annotations checked ad hoc (`ModelState.IsValid` is explicitly checked only in the `image/coordinates` upload action — the other actions rely on custom exceptions for missing required fields, not `ModelState`). **Client-side** (`src/web`): no schema validation library; the only client check is a file-type/size implicit constraint via the native file picker — no explicit min/max enforced in `UploadButton.tsx`.

### 8.3 Stored procedures (`mytravels.domain/Features/PointOfInterest/*.sql`)

`spGetPointOfInterest()` (no args — fetch all), `spGetPointOfInterestById(id)`, `spUpdatePointOfInterestTags(json)`. Invoked via `FromSqlRaw`/`FromSqlInterpolated`/`ExecuteSqlInterpolatedAsync` in `CoreDbContext.cs`. The three `spGetPointOfInterest*` functions were updated to project `Description` and `CorrelationId` (migration `20610930080000_AddCorrelationIdToPointOfInterestFunctions`) — adding a column to `PointOfInterests` that the read path must expose means editing these `.sql` files **and** adding a migration that re-runs them, not just the entity.

**`spGetPointOfInterestByTagName(tagName)` was dropped** with the SOLR work (`api:v1.0.11`). Its `.sql` file is deleted, so a fresh database never creates it, and migration `20620930080000_DropPointOfInterestByTagNameFunction` removes it from databases that already have it. That migration is deliberately dated *after* `20610930080000_AddCorrelationIdToPointOfInterestFunctions`, which replays every remaining `.sql` file — the same future-dating convention `UpdateStoredProcedure` and `SeedData` use to control ordering.

There is **no** stored procedure for POI search at all any more: search and the tag filter both go to SOLR (§6.5, §9). `spGetPointOfInterest()` is still load-bearing for it, though — it is what a full reindex reads, so its output shape determines what ends up in the index.

---

## 9. State Management & Persistence

- **PostgreSQL 17.6** — sole system of record for POI metadata, tags, and audit logs. `CoreDbContext` is configured **globally `NoTracking`** (`QueryTrackingBehavior.NoTracking`), meaning any write path must manually attach/mark entities modified — `UpdateAddressAsync` does this explicitly (`EntityState.Unchanged` + per-property `IsModified = true`).
- **MinIO (S3-compatible)** — binary image storage, two buckets: `uploaded-images` (originals) and `resized-images` (thumbnails), both lazily auto-created on first write, not at startup.
- **Apache SOLR 9.10.1** — a **derived** store, not a system of record: the sole backend for POI search **and, since `api:v1.0.12`, for the POI list the map draws**, rebuildable from PostgreSQL at any time. Single instance, one collection (`mytravels-pois`), no SolrCloud and no replicas; a PVC (Compose volume in stages 0–2, `local-path` PVC in stages 3–4) is the only durability, and `POST /api/pointofinterest/reindex` is the recovery path.
  - **Document key is `PointOfInterestKey`, not `PointOfInterestId`.** Replacing a photo inserts a *new* row under the same key (`AddImageToPointOfInterestAsync` sets `Id = 0` and resets `ImageResized` to `false`, since the entity is re-inserted rather than built fresh and would otherwise carry the superseded row's flags — §8.1), so keying on `Id` would leave the superseded row in the index permanently and a rebuild — which reads the latest-per-key `spGetPointOfInterest()` — would silently disagree with incremental indexing. `poi_id` is carried as an ordinary field so DTO mapping still returns the right `Id`.
  - **Both write paths resolve latest-row-per-key before indexing.** `SolrIndexService` does this for a single POI and for a batch, so indexing is idempotent and independent of message ordering — two `index-solr` messages for the same key processed out of order converge on the same document.
  - **Written three times per upload.** At `CreatePointOfInterestAsync` the address, description and tags are all still empty, so `index-solr` is published once at creation (the POI becomes findable by date and coordinates immediately) and again at the end of `AppendFormattedAddress` and of `AppendImageTags`. `ResizeImage` does **not** index directly — it chains `append-image-tags`, so the write that carries the description and tags is the one `AppendImageTags` publishes after the Anthropic call has succeeded and both fields are persisted. SOLR upserts by key, so repeat indexing costs nothing but the round trip.
  - **Schema is applied at runtime through the Schema API, not a mounted configset.** `SolrIndexService.EnsureSchemaAsync` adds any missing field idempotently and tolerates "already exists"; `SolrSchemaInitializer` (hosted service in `messaging`) calls it at startup in the background with exponential backoff, because neither Compose `depends_on` nor a k8s readiness probe guarantees the collection exists yet. One definition in C# instead of a bind mount in Compose plus a ConfigMap in stages 3 and 4.
  - **Fields**: `id` (the `PointOfInterestKey`, uniqueKey), `poi_id`, `formatted_address`, `tags` (multiValued, tokenised), `tag_exact` (multiValued string — written on every document but **read by nothing since `api:v1.0.12`**, when the `/filter` endpoint it served was deleted; kept deliberately so an exact-tag filter can be reintroduced without a reindex), `tag_ids` (multiValued int), `description`, `date_taken`, `date_created`, `latitude`, `longitude`, `container`, `original_file_name`, `generated_blob_name`, `image_resized`, `correlation_id`. `tags`/`tag_exact`/`tag_ids` are written in the same order and must stay in sync — both `ToDto` helpers discard any tag whose `TagId` is null, so the search side has to pair a name back up with its PostgreSQL id.
  - **No delete propagation.** No delete endpoint exists on `PointOfInterestController`, so nothing removes documents except a purge-and-rebuild. Adding a delete endpoint means publishing `index-solr` with a delete flag.
- **RabbitMQ** — transient message bus for async work; no persistence guarantees beyond `durable: true` queue declaration (Observed in `MessageSubscriberBase.cs`) — no DLX, so message loss/poison-loop risk exists (§12, §17).
- **No caching layer** (no Redis/in-memory cache) anywhere in the stack.
- **Transaction boundaries**: each EF Core `SaveChangesAsync` call is its own implicit transaction; no explicit multi-statement `BeginTransaction`/`Commit` usage found in `CoreDbContext.cs`. `EnableRetryOnFailure()` is set on the Npgsql provider (transient-fault retry, not app-level transactions).
- **Ordering guarantees**: none — RabbitMQ fanout with a single unordered queue per exchange; concurrent processing of `resize-image` and `append-formatted-address` for the same POI can interleave arbitrarily (they touch different columns, so this is currently benign). The same absence of ordering is why `SolrIndexService` re-resolves the latest row for a key instead of trusting the id on the message.
- **Read-your-writes**: there is none between PostgreSQL and SOLR, and since `api:v1.0.12` there is no PostgreSQL read path left to paper over it. A POI is committed to PostgreSQL as soon as the upload returns, but appears in neither `GET /api/pointofinterest` nor `/search` until the `index-solr` consumer has run, and carries no address/description/tags until the corresponding enrichment subscriber has finished and republished. The web app absorbs this by polling the list after an upload. Updates commit with `commit=true` per batch, so there is no soft-commit delay on top of that.

---

## 10. Configuration & Environment

### 10.1 `mytravels.api` (`appsettings.json` / `appsettings.Development.json`)

| Key | Default (appsettings.json) | Required? | Breaks if missing |
|---|---|---|---|
| `CorsHosts` | `*` | effectively yes | `NullReferenceException` at startup (`.Split(';')` on a null value) if the key is entirely absent from config |
| `ConnectionStrings:CoreDbContext` | `Host=localhost;...;Username=user123;Password=password123` | yes | `InvalidOperationException` at DI registration |
| `RabbitMQ:Uri` | `amqp://user123:password123@localhost:5672` | yes | connection factory throws on first use |
| `MinIO:Endpoint` / `AccessKey` / `SecretKey` | `localhost:9000` / `user123` / `password123` | yes for any storage call | `MinioClient` build fails / calls throw |
| `Solr:Url` | `http://localhost:8983` | yes for any search | search and the tag filter throw; nothing else is affected |
| `Solr:Collection` | `mytravels-pois` | yes for any search | as above |
| `Solr:TimeoutSeconds` | `30` | no | falls back to 30 when ≤ 0 |
| `Solr:BatchSize` | `500` | no (indexer-only, so unused by `api`) | falls back to 500 when ≤ 0 |
| `Flagsmith:ApiUri` | `http://localhost:8000/api/v1/` | yes for flag evaluation | `FlagsmithProvider` construction throws (`ApiUri` is passed through `new Uri(...)!`) |
| `Flagsmith:ServerSideEnvironmentKey` | `<YOUR_FLAGSMITH_SERVER_SIDE_ENVIRONMENT_KEY>` (placeholder) | yes for flag evaluation | every `GetBooleanValueAsync` call falls back to its supplied default (`true`) rather than throwing — `enable-poi-search`/`enable-message-tracing` behave as always-on |
| `GoogleApiKey` | `<YOUR_GOOGLE_API_KEY>` (placeholder) | no — placeholder triggers automatic fallback to OpenStreetMap | geocoding silently switches provider, not an error |
| `GoogleMapsUrl` | `https://maps.googleapis.com` | only if Google provider active | |
| `GooglePlacesUrl` | `https://places.googleapis.com` | **dead config — never read** | n/a |
| `AllowedHosts` | `*` | no | ASP.NET Core default host-filtering |

The stale `AnthropicApiKey` placeholder was **removed** from `mytravels.api/appsettings.json` with the SOLR work; `api` still DI-registers `IImageDescriptionService` to satisfy `PointOfInterestService`'s constructor but never calls it, and the live setting remains on `messaging` only (§10.2).

### 10.2 `mytravels.messaging` — same `appsettings.json` shape and defaults as `mytravels.api`, plus the **only live Anthropic configuration** in the system:

| Key | Default | Required? | Breaks if missing |
|---|---|---|---|
| `AnthropicApiKey` | `<YOUR_ANTHROPIC_API_KEY>` (placeholder) | yes, for the description/tagging feature | every `append-image-tags` message throws `ApiException(503, …)`, retries 3×, and dead-letters (§7.5) — POIs still get addresses and thumbnails |
| `AnthropicModel` | `claude-haiku-4-5` (code fallback in `AnthropicImageDescriptionService`) | no | falls back to the default model |
| `Solr:Url` / `Solr:Collection` / `Solr:TimeoutSeconds` / `Solr:BatchSize` | `http://localhost:8983` / `mytravels-pois` / `30` / `500` | yes for indexing | `SolrSchemaInitializer` retries 10 times with backoff and then gives up with an error log rather than crashing the host; `index-solr` and `reindex-solr` messages retry 3× and dead-letter. `BatchSize` is the reindex page size and matters only here |
| `Flagsmith:ApiUri` / `Flagsmith:ServerSideEnvironmentKey` | same shape and same environment key value as `api` (§10.1) — Flagsmith's server-side key is per-environment, not per-service | yes for flag evaluation | `AppendImageTags` falls back to its supplied default (`true`) for `enable-image-description` rather than throwing, same fail-open behaviour as `api` |

Deployment wiring: `ANTHROPIC_API_KEY` / `ANTHROPIC_MODEL` in `.env` → `AnthropicApiKey` / `AnthropicModel` on the `messaging` Compose service (stages 1–2); the `messaging` **Secret** (key) and **ConfigMap** (model) in stages 3–4. The split is deliberate — the model name is not a secret. Neither key appears on `api` or `mcp` in any manifest or Compose file. `Flagsmith:ServerSideEnvironmentKey`, by contrast, **is** shared between `api` and `messaging` (both need it) but not `mcp` (which registers no Flagsmith client at all).

### 10.2a `mytravels.mcp`

Same `appsettings.json` shape as api/messaging (`ConnectionStrings:CoreDbContext`, `RabbitMQ:Uri`, `MinIO:*`, `Solr:*`, `GoogleApiKey`, `GoogleMapsUrl`, `GooglePlacesUrl`, `AllowedHosts`) with the same committed `user123`/`password123` local-dev defaults — **minus `CorsHosts`**, which it neither defines nor reads (it registers no CORS policy, consistent with having no browser client). Its configuration chain (`SetBasePath` → `appsettings.json` → environment variables → `AddUserSecrets<Program>()`) is character-for-character the same as `mytravels.api`'s; `mytravels.messaging` is the odd one out, building no explicit chain and calling no `AddUserSecrets`.

### 10.2b OpenTelemetry configuration (api, mcp, messaging)

| Key | Default | Notes |
|---|---|---|
| `OTEL_SERVICE_NAME` | `mytravels-api` / `mytravels-mcp` / `mytravels-messaging` (hardcoded per-service fallback) | read via `builder.Configuration["OTEL_SERVICE_NAME"]` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset → OTel SDK default `http://localhost:4317` | set to `http://otel-collector:4317` in stage 1 Compose and in the stage 3/4 Deployments; **set nowhere in stages 0 and 2**, where export therefore fails against a non-existent local collector (non-fatal; produces connection-refused noise) |

All three services call `.UseOtlpExporter()` with `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddRuntimeInstrumentation` (metrics) and `AddSource("Npgsql")` (traces). There is no sampling configuration and no `OTEL_TRACES_SAMPLER` handling — every request is traced.

### 10.3 `mytravels.migration` — `ConnectionStrings:CoreDbContext` only, plus whatever `--connection` argument is passed to the built `efbundle` at runtime (Kubernetes Job overrides via `$(ConnectionStrings__CoreDbContext)` env var, `4-argocd/manifests/migrations/job.yaml`).

### 10.4 `mytravels.storage`

`MinIOConfig`: `Endpoint`, `AccessKey`, `SecretKey` (bound from `MinIO` section). Azure path reads `StorageAccountConnectionString` directly via raw `IConfiguration.GetValue` — **this key exists in no `appsettings.json` in the repo**, consistent with `AzureStorageService` being fully dead code (§17, F-1).

### 10.5 `web` (Vite build-time env)

`VITE_API_BASE_URL` — baked into the static bundle at Docker build time via `ARG`/`ENV`. Defaults to `http://localhost:5101` if unset (`src/web/src/api/client.ts:3`).

`VITE_OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` — same mechanism, read at `src/web/src/telemetry.ts:10`. **No default**: the whole OpenTelemetry setup sits behind `if (otlpEndpoint)` (`telemetry.ts:17`), so an unset build arg makes that branch statically false and Vite tree-shakes the entire browser-RUM block out of the bundle. (The historical `mytravels-web:v1.0.9` image was built that way and shipped with no browser tracing; `v1.0.7` fixed it by passing a non-empty sentinel, below.)

Neither value is runtime-configurable *in the image*, but as of `v1.0.7` both are runtime-configurable *at deploy time* in the Kubernetes stages: the image is built with the sentinel placeholders `__API_BASE_URL__` and `__OTLP_TRACES_ENDPOINT__`, and a `render-config` init container copies the docroot into an `emptyDir`, `sed`s the sentinels to values from the `web-config` ConfigMap, and mounts that over nginx's docroot (`3-kubernetes/manifests/web/{1-configmap,2-deployment}.yaml`, `4-argocd/manifests/web/{configmap,deployment}.yaml`). Both sentinels are non-empty precisely so the tree-shaking above does not fire. Stage 1 is unaffected — it builds the image locally from source with literal URLs passed as build args. Stage 2 now runs the same `v1.0.7` sentinel image as the k8s stages, with a Compose equivalent of the init-container trick: a one-shot `render-web-config` service `sed`s the real URLs (from `.env`, not a ConfigMap) into a shared `web-docroot` volume before the `web` service mounts it over its own docroot (`2-dockerhub/docker-compose.yml`).

### 10.6 Docker Compose / Kubernetes env-var surface (cross-stage, from deployment-topology research)

| Variable | Present in Compose stages? | Present in k8s manifests? |
|---|---|---|
| `GOOGLE_API_KEY` | yes (1, 2) | **no — dropped entirely, Finding F-14** |
| `ANTHROPIC_API_KEY` / `ANTHROPIC_MODEL` | yes (0–2 `.env.example`), bound onto the `messaging` service only | `messaging` Secret (key) + `messaging` ConfigMap (model) in 3 and 4; `4-argocd/.env.example` carries `ANTHROPIC_API_KEY` as input to the out-of-band `kubectl create secret` step |
| `GRAPH_API_TOKEN` | yes (`.env.example`, stages 0–3) | no — and never consumed by any Compose service either; dead variable |
| `VITE_API_BASE_URL` | yes, value differs per stage (`http://localhost:5101` in 1/2); in stage 2 it's substituted at deploy time by the `render-web-config` Compose service rather than baked in at build time | `web-config` ConfigMap in 3 and 4, substituted into the built bundle by the `render-config` init container (`http://api.mytravels.local:8080`) |
| `VITE_OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` | stage 1 (build arg, `http://localhost:4318/v1/traces`) and stage 2 (deploy-time substitution via `render-web-config`, same value) | `web-config` ConfigMap in 3 and 4 (`http://otel.mytravels.local:8080/v1/traces`) |
| `MINIO_ENDPOINT` / console port | consistent `9090:9090` everywhere | consistent, `minio-console` Service port `9090` |
| `SOLR_URL` / `SOLR_COLLECTION` | yes (0–2 and 4 `.env.example`), bound onto api/mcp/messaging as `Solr__Url`/`Solr__Collection` in stages 1–2. Stage 0 carries `http://localhost:8983` because the app runs from source on the host while SOLR runs in Compose | not env vars — set inline as literal values (`http://solr:8983`, `mytravels-pois`) on the api/mcp/messaging Deployments in stages 3–4, since neither is a secret |
| `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_SERVICE_NAME` | stage 1 only (`http://otel-collector:4317`); absent in 0 and 2 | set inline on the api/mcp/messaging Deployments in stages 3–4 |
| Argo CD-only vars (`ARGOCD_USER`, `ARGOCD_PASSWORD`) | only in `4-argocd/.env.example` | applied out-of-band via `kubectl create secret`, not committed |

Stage 4's `.env.example` is structurally distinct from stages 0–3 (drops most app config vars since it never builds images or runs Compose — `.env` there is purely input to a "create the k8s secrets" runbook step).

---

## 11. Authentication, Authorization & Security

**Observed:** there is **no authentication or authorization anywhere in the application layer.**
- No `AddAuthentication`/`AddAuthorization`/`UseAuthentication`/`UseAuthorization` in `mytravels.api/Program.cs`, `mytravels.mcp/Program.cs`, or `mytravels.messaging/Program.cs`.
- No `[Authorize]` attribute exists anywhere in the solution.
- Every REST endpoint, including image upload, is fully anonymous.
- **Every MCP tool is likewise anonymous**, and the MCP server gets its own ingress host (`mcp.mytravels.local`) in stages 3–4. `ModelContextProtocol.AspNetCore` is registered with no auth handler, so anything that can reach port 5103 can write points of interest and drive the geocoding provider. This widens the unauthenticated write surface from one service to two.
- Swagger UI is exposed unauthenticated in every environment (not gated behind `IsDevelopment()`).
- CORS policy `AllowSpecificOrigin` uses `AllowAnyHeader()`+`AllowAnyMethod()`, restricted only by the `CorsHosts` origin list (default `*` in `appsettings.json`, meaning **any origin** unless overridden).

**Secrets handling:**
- Local dev credentials (`user123`/`password123` for DB/RabbitMQ/MinIO) are committed in plaintext in `appsettings.json` across `mytravels.api`, `mytravels.mcp`, `mytravels.messaging`, and `mytravels.migration`.
- `appsettings.Development.json` (genuinely gitignored, `.gitignore:59`) contains a real-format Google Maps API key on this machine — not committed, consistent with the April 2026 secret-scrub incident that added this pattern.
- **⚠️ All eight stage-3 Secret manifests are committed to git, not ignored (Finding F-18).** `.gitignore` (132 lines) contains **no `secret` rule of any kind** — an earlier revision of this spec asserted a `*secret.yaml` pattern; no such line exists and none ever matched anything. `git ls-files | grep secret` returns `3-kubernetes/manifests/{api,mcp,messaging,migrations,minio,postgres,rabbitmq}/1-secret.yaml` and `observability/16-grafana-secret.yaml`.
- Most of those hold only the `user123`/`password123` tutorial credentials, which is harmless by design. **`3-kubernetes/manifests/messaging/1-secret.yaml` did not**: it carried a real-format Azure Content Safety endpoint (`…cognitiveservices.azure.com`) and an 84-character API key, base64-encoded, introduced in commit `a270e7d` (2026-08-21). Base64 is encoding, not encryption. Those two keys (and the matching `ContentSafetyEndpoint`/`ContentSafetyKey` env entries in the messaging Deployment manifests, `.env.example`, and both stage-3/4 runbooks) were removed from the tracked files in `2318b4d` — but **verified as of 2026-09-13, both values are still readable at `git show a270e7d:3-kubernetes/manifests/messaging/1-secret.yaml`.** This history predates no scrub: the April 2026 `git filter-repo` pass cleaned earlier secrets, and this credential was committed months after it. Treat the key as compromised: rotate it at the provider, and scrub history again if that matters for this repo. The file's current `AnthropicApiKey` entry is a base64 placeholder, not a live key.
- Each stage's `.env` (as opposed to `.env.example`) is correctly gitignored (`.gitignore:97-100`, `.env` + `.env.*` with `!.env.example`). The working copies on this machine contain a live-format Anthropic API key; none of them is tracked, and `git log -S 'sk-ant-api03'` finds nothing in history.
- `3-kubernetes/manifests/mcp/1-secret.yaml` follows the same committed pattern as its siblings, but holds only tutorial credentials (DB connection string, RabbitMQ URI, MinIO keys).
- Stage 4 (`4-argocd`) removes **all** Secret manifests from the repo entirely, applying them out-of-band via `kubectl create secret` from `.env` — a deliberate, documented fix over stage 3's pattern. Given the above, this is the stage-3 problem's actual remedy, not merely a stylistic upgrade: stage 3's secrets are structurally unprotected, not "gitignored per-file".
- Argo CD is configured with `server.insecure: true` (TLS disabled) and a local admin-equivalent account (`user123`, `role:admin`), intended for the local k3d tutorial context, not production.
- No PII handling policy is evident; uploaded photos may contain identifying metadata (EXIF), which is read (for GPS) but not stripped before storage — thumbnails and originals both retain any other embedded EXIF fields.
- **SOLR is unauthenticated**, like PostgreSQL, RabbitMQ and MinIO in this stack, and is additionally published on its own ingress host (`solr.mytravels.local`) in stages 3–4 so the Admin UI is reachable — the same trade-off already made for the RabbitMQ and MinIO consoles. Anything that can reach port 8983 can read every indexed POI and purge or rewrite the whole collection. Nothing in the index is a secret that is not already exposed through the anonymous REST API, and a purge is recoverable via `POST /api/pointofinterest/reindex`, but the write surface is real.
- No rate limiting, no CSRF protection (not applicable to a token-less anonymous JSON API, but also means no anti-automation protection on the upload endpoint).

---

## 12. Error Handling & Retries

**API (`mytravels.api`):** centralized in `ApiExceptionMiddleware`. Custom exception → HTTP status: `RequiredParameterNotFoundException`→403 (semantically should be 400), `OutOfRadiusException`→403, `DataNotFoundException`→404, `ApiException`→its own status, everything else→500 with the raw `Exception.Message` serialized to the client (information disclosure risk). All client and server errors are logged at `LogError` level (no severity differentiation).

**EF Core:** `EnableRetryOnFailure()` is set on the Npgsql provider for all three DbContext-owning projects (api, messaging, migration) — transient network/connection retry, count/backoff not overridden from Npgsql defaults (Observed: no explicit retry-count argument passed).

**Geocoding (Polly, inline in `GoogleMapsService`/`OpenStreetMapsService`):** `WaitAndRetryAsync(2, i => TimeSpan.FromSeconds(Math.Pow(2, i)))` — 2 retries, exponential backoff (~2s, ~4s), no jitter, no circuit breaker, no overall timeout policy.

**RabbitMQ consumers (`MessageSubscriberBase<T>`):** application-level bounded retry, not broker-level. Still **no dead-letter exchange, no TTL, no max-length** on the queue — the base class implements the policy itself:

1. On success: `BasicAck` + a `ConsumeSucceeded` audit row.
2. On exception with `x-retry-count < 3`: republish the original body to the **same** exchange with `x-retry-count + 1` (other headers copied across), ack the original, write a `Retried` row. There is **no delay or backoff** — the redelivery is immediate, so three attempts are consumed as fast as the consumer can fail.
3. On exception with `x-retry-count >= 3`: publish a `FailedMessage` to the paired `<exchange>-failed` fanout exchange, ack the original, write a `Failed` row. Since nothing binds a queue to those exchanges (§6.4), the `FailedMessage` is discarded on publish and the audit row is the only durable record.
4. A payload that deserializes to `null` is acked and dropped with a warning, never retried.

The retry counter lives in a header on a republished copy, so it does not survive a broker restart in-flight and a redelivery of the *original* (unacked at crash time) restarts the count at zero. All three subscribers now log inside a `catch` and **rethrow** — rethrowing is load-bearing, since the base class's retry/dead-letter path only runs if the exception reaches it.

**Sweeper (`CronJobBase`):** per-tick try/catch logs and swallows exceptions so one bad tick doesn't kill the 30-minute loop; the first run (before the timer loop) is wrapped in the same guard. `AppendFormattedAddressSweeper` additionally try/catches each POI so one unresolvable row doesn't abort the sweep. Sweep failures remain easy to miss without active log monitoring — they write no audit rows, since the sweeper bypasses RabbitMQ entirely.

**Audit logging:** every audit write (publish- and consume-side) is individually wrapped; a failure is logged at warning level and swallowed. Message processing therefore never fails *because* of traceability, and an incomplete timeline in the UI is not proof that a step did not run.

**Idempotency:** `AppendFormattedAddress` and `ResizeImage` short-circuit if their target field is already set (`FormattedAddress` non-empty / `ImageResized == true`), making at-least-once delivery safe for the steady-state case. In `ResizeImage` the short-circuit covers only the resize work — its `append-image-tags` publish runs on both paths, so a redelivery against an already-resized row still drives the description/tag/index chain instead of dropping it. **`AppendImageTags` has no such guard** — a redelivery re-calls the Anthropic API and rewrites `Description`, which costs a request per retry and makes the non-idempotent path the one that talks to a metered third party. None of this protects against the concurrent-write race in Finding F-12.

**Storage layer:** `MinIOStorageService.ListObjectsAsync` and `RemoveObjectAsync` both catch generic `Exception`, `Console.WriteLine` it, and return normally — callers get no signal that a list is incomplete or a delete silently failed.

**Frontend:** `App.tsx`'s initial load and polling fetches both swallow errors with empty catch handlers — no user-visible error/retry UI if the API is unreachable (the map simply stays empty). `UploadButton.tsx`'s catch block discards the underlying `Error` (which does carry the real HTTP status/body from `throwForStatus`) and shows one static failure message regardless of cause.

---

## 13. Concurrency, Async & Scheduling

**Hosted services (`mytravels.messaging`, all registered in `Program.cs`, run concurrently in one process):**
1. `AppendFormattedAddress` — RabbitMQ consumer, `prefetchCount: 10`, but internally serializes all processing behind a `static SemaphoreSlim(1,1)` — the prefetch headroom is never actually used; messages are handled one at a time.
2. `AppendFormattedAddressSweeper` — `CronJobBase`-derived, runs immediately on startup then every 30 minutes via `PeriodicTimer`, for the life of the process.
3. `ResizeImage` — same consumer pattern/prefetch/semaphore serialization as #1.
4. `AppendImageTags` — same pattern again, and the semaphore matters most here: it serializes the Anthropic calls, so description/tagging throughput is one image at a time per `messaging` replica regardless of prefetch.
5. `FailedExchangeDeclarer` — not a consumer. Opens one connection at startup, declares the three `-failed` exchanges, closes it, and returns; `StopAsync` is a no-op. It runs concurrently with the subscribers' own `StartAsync`, which redundantly declare the same exchanges with the same arguments (idempotent, so the race is benign).

Each of the three semaphores is `static` **per subscriber class**, so the three pipelines run in parallel with each other while each is internally serial. Two `messaging` replicas would therefore process in parallel with no cross-process coordination.

**Race condition (Finding F-12):** `AppendFormattedAddress` guards itself with a semaphore local to that class; `AppendFormattedAddressSweeper` has **no locking at all** and runs in the same process concurrently — a POI could be read as unresolved by both simultaneously, geocoded twice, and written twice. No optimistic-concurrency token exists on `PointOfInterest` to catch this at the DB layer.

**Connection lifecycle:** `MessagePublisher.PublishAsync` opens a brand-new AMQP connection + channel **per publish call**, declares the exchange, publishes, and closes both — no pooling/reuse, a latency/throughput concern under any real load. Separately, `Program.cs` also registers a DI-singleton `IConnectionFactory` that only `MessagePublisher` consumes, while `MessageSubscriberBase` builds its own `ConnectionFactory` independently from raw config — duplicated construction logic, not shared.

**No locking/queueing at the database layer** beyond what Postgres's MVCC provides implicitly; no `SELECT ... FOR UPDATE` or EF Core concurrency tokens anywhere in `CoreDbContext.cs`.

---

## 14. External Integrations & Third-Party Dependencies

| Integration | Endpoint | Auth | Failure mode |
|---|---|---|---|
| Anthropic Claude (image description/tagging) — called **only from `messaging`**, model configurable via `AnthropicModel`, default `claude-haiku-4-5` | `api.anthropic.com` | API key (`AnthropicApiKey`, passed to SDK) | 429 (rate limit) → `ApiException(429, ...)`; 5xx or other API error → `ApiException(502, ...)`; refusal → `ApiException(422, ...)`; unset/placeholder key → `ApiException(503, "service not configured")`. No Polly policy of its own — every one of these becomes a consumer failure and inherits the base class's 3 immediate retries, then dead-letters (§12). Nothing surfaces to an HTTP client, since there is no synchronous caller |
| Google Maps Geocoding API | `GoogleMapsUrl` config (`https://maps.googleapis.com`) | API key (`GoogleApiKey` query/header) | Polly: 2 retries, exp. backoff; `InvalidOperationException` thrown on non-OK or missing `formatted_address` after retries exhausted |
| OpenStreetMap Nominatim (fallback geocoder, used when `GoogleApiKey` is unset/placeholder) | `OpenStreetMapsUrl` config, default `https://nominatim.openstreetmap.org` | none (public API), `User-Agent: mytravels/1.0` default | same Polly retry pattern |
| OpenStreetMap tile server (frontend only) | `https://tile.openstreetmap.org/{z}/{x}/{y}.png` | none | no fallback/rate-limit handling if OSM throttles |
| MinIO (S3-compatible) | `MinIO:Endpoint` config | static access/secret key | swallowed exceptions on list/remove (see §12) |
| Apache SOLR 9.10.1 (POI search and indexing; called from `api`, `mcp` and `messaging`) | `Solr:Url` + `Solr:Collection`, default `http://localhost:8983` / `mytravels-pois`; `http://solr:8983` in every deployed stage | **none — unauthenticated, like every other backing service here (§11)**, and exposed on `solr.mytravels.local` in stages 3–4 for the Admin UI, for the same reason the RabbitMQ and MinIO consoles are | Plain HTTP over Flurl — no SolrNet dependency. Polly on `/select` and `/update`: 2 retries, exponential backoff, matching `GoogleMapsService`. A read failure surfaces to the HTTP caller as a 500; an index failure is a consumer failure and inherits the 3-retry/dead-letter path (§12). The Schema API call is deliberately un-retried by Polly because `SolrSchemaInitializer` owns that backoff itself (10 attempts, capped at 30s) and logs an error rather than crashing the host if SOLR never appears |
| Flagsmith 2.261.0 (feature flags; evaluated from `api` and `messaging` via the OpenFeature .NET SDK — `OpenFeature.Contrib.Providers.Flagsmith` v0.3.1 — and from `web` via `@openfeature/web-sdk` / `@openfeature/react-sdk` / `@openfeature/flagsmith-client-provider`) | `Flagsmith:ApiUri` (`api`/`messaging` config section, e.g. `http://flagsmith:8000/api/v1/`) + `Flagsmith:ServerSideEnvironmentKey` on the .NET side; `VITE_FLAGSMITH_ENVIRONMENT_ID` (client-side key) baked into the `web` bundle. Self-hosted, shares `mytravels-postgres` in a second database (`FeatureDb`), not a second container or schema | **none — unauthenticated, like every other backing service here (§11)** | **Inferred** (from the OpenFeature spec's error-handling contract, not independently verified against a live outage): `FeatureClient.GetBooleanValueAsync(key, true)` on .NET falls back to the supplied default (`true`) if Flagsmith is unreachable, so a Flagsmith outage fails open rather than 404ing every gated endpoint. `mcp` never registers a client and evaluates none of the three flags |
| Azure Blob Storage | never invoked — `AzureStorageService` is unregistered dead code | connection-string (never configured) | n/a — unreachable code path |
| RabbitMQ | `RabbitMQ:Uri` | username/password in URI | no broker-level DLX; poison messages are bounded by the application-level 3-retry policy and then discarded to an unbound `-failed` exchange (§12) |
| OTLP collector (traces + metrics from api/mcp/messaging, and browser RUM from `web`) | `OTEL_EXPORTER_OTLP_ENDPOINT`, default `http://localhost:4317`; browser posts OTLP/HTTP to `otel.mytravels.local` in stages 3–4 | none | export failures are non-fatal and logged by the OTel SDK; absent in stages 0 and 2, where the default endpoint resolves to nothing |
| MCP clients (LLM hosts) | inbound to `mcp` on 5103, streamable HTTP via `MapMcp()` | **none — fully anonymous** | tool errors returned as `McpException`, except the gap noted in F-17 |

---

## 15. Runtime Behaviour & Edge Cases

- **Startup (api/messaging):** DI container built, EF Core context registered (throws immediately if connection string missing), RabbitMQ `IConnectionFactory` built (lazy — doesn't connect until first use), culture forced to `InvariantCulture` process-wide. No explicit readiness gate on RabbitMQ/Postgres availability at startup beyond `EnableRetryOnFailure()`; in Kubernetes, ordering is enforced externally via Job/init-container dependencies (migration Job runs before api/messaging Deployments are expected to work, though nothing blocks them from starting concurrently).
- **Health/liveness:** api's `livenessProbe` hits `/swagger` (not a dedicated health endpoint); messaging's `livenessProbe` is commented out in both k8s manifest sets; neither has a `readinessProbe`. `mcp` is the only application service with a purpose-built health endpoint — `GET /health` → `200 "mcp is running..."`, which its `livenessProbe` targets (still no `readinessProbe`). Only `web`'s Deployment has both probes, checking `/`. Within the observability stack, Prometheus, Tempo, and Grafana all define proper `readinessProbe`s, so the tutorial's own infrastructure is better instrumented than the app it observes. The `solr` Deployment added in stages 3–4 follows that better pattern too, and deliberately splits the two questions: `livenessProbe` hits `/solr/admin/info/system` (is SOLR up?) while `readinessProbe` hits `/solr/mytravels-pois/admin/ping` (does the collection exist and answer?).
- **Empty input:** uploading with no `image` file → `RequiredParameterNotFoundException` → HTTP 403 (not 400).
- **Malformed/no-EXIF image:** on `POST /image` this is now a hard failure — `InvalidOperationException("Image is not geocoded")` → HTTP 500, with the uploaded blob already written to MinIO and left orphaned. On `POST /image/coordinates` an unreadable image is tolerated (`ImageProcessingException` caught, `DateTaken` left null) because the client supplied the location. The frontend's `hasCoordinates` filter still exists but should no longer see `(0,0)` rows from new uploads.
- **Oversized input:** no explicit request body size limit configured anywhere found in `Program.cs` (relies on ASP.NET Core/Kestrel defaults).
- **Network failure to geocoding provider:** 2 Polly retries, then the `InvalidOperationException` propagates out of `ProcessMessageAsync` into `MessageSubscriberBase<T>`, which retries the whole message up to 3 more times (no backoff) and then dead-letters it — so a sustained provider outage costs up to 4 × (1 + 2 Polly) attempts per POI before the message is abandoned, with a `Failed` audit row as the trace.
- **Anthropic unconfigured:** every `append-image-tags` message fails 503 → 3 retries → `Failed`. Uploads still work and still get addresses and thumbnails; only `Description`/tags are absent, and the traceability UI fills with failed correlations.
- **Partial failure (any one of resize / address / tagging fails):** the three are fully independent messages and consumers, so a POI can end up with any subset of thumbnail, address, and description. Unlike before, a non-transient failure no longer loops forever — it terminates after 3 retries, leaving the POI permanently partial with no automatic recovery (the sweeper covers only the address case, and only for 2 days).
- **Shutdown:** no explicit graceful-shutdown/drain logic found in either hosted-service class; relies on ASP.NET Core's default `IHostedService.StopAsync` behavior (cancellation token propagation into the consumer loop's blocking wait).

---

## 16. Assumptions, Implicit Behaviour & Undocumented Conventions

- **Provider auto-selection is silent:** whichever of Google Maps or OpenStreetMap gets registered as `IMapsService` is decided entirely by whether `GoogleApiKey` equals the literal placeholder string `"<YOUR_GOOGLE_API_KEY>"` — there is no log line or startup banner announcing which provider is active.
- **`0` is overloaded** as both "valid coordinate on the equator or prime meridian" and "no EXIF GPS". The upload guard rejects a photo if *either* coordinate is `0` (`Latitude == 0 || Longitude == 0`), so a genuinely on-axis location cannot be uploaded through the EXIF path at all — it must go through the place-picker endpoint. The frontend's `hasCoordinates` check survives from the era when such rows were created rather than rejected, and is now effectively dead for new data.
- **The traceability UI is a snapshot, not a live feed** — `TraceabilityPage` fetches once on mount with no polling, while `MapPage` polls every 3s. A correlation observed mid-flight needs a manual refresh to advance.
- **The migration image's actual behavior is not visible from `Program.cs`** — a reader following only that file would conclude migrations are never applied; the real mechanism is a build-time `dotnet ef migrations bundle` producing a separate `efbundle` native executable that becomes the container's entrypoint (§5, §17 F-3).
- **Stored-procedure SQL is deployed via EF Core migrations** (`Features/**/*.sql` executed inside migration `Up()` methods), not tracked/versioned as ordinary application code — changing a stored procedure requires a new migration, not just editing the `.sql` file.
- **The web frontend's API base URL is fixed per Docker image build only in stage 1** (built locally with literal URLs as build args, so a new image is needed per target URL there). Stages 2–4 all now use the `v1.0.7`+ sentinel-placeholder image with a deploy-time substitution step (Compose's `render-web-config` service in stage 2, the `render-config` init container in stages 3–4), so the same built image is reused across environments — see §10.5.
- **`mytravels.migration`'s `Program.cs`/`Host` is only a design-time vehicle** for EF Core CLI tooling to discover `CoreDbContext` — it is never itself executed as an application at runtime.

---

## 17. Findings: Bugs, Inconsistencies, Dead Code, Risk

### Bugs / probable defects

- **F-2** — `GET /api/pointofinterest/{id}?resizedImage=bool` accepts `resizedImage` but never passes it to the service call; the parameter is dead and the endpoint always returns the same image regardless of the flag. `src/api/mytravels.api/Controllers/PointOfInterestController.cs:46-50`.
- **F-6** — `RequiredParameterNotFoundException` and `OutOfRadiusException` both map to HTTP 403 Forbidden instead of 400 Bad Request — semantically wrong for "missing/invalid input," and affects every action that validates required params. `src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs:37,41`.
- **F-8** — Unhandled-exception responses serialize the raw `Exception.Message` straight into the client-facing JSON body — information disclosure of internal state (DB errors, stack details embedded in messages). `src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs:63`.
- **F-11** — `AppendFormattedAddressSweeper` filters on `FormattedAddress == ""` (strict empty-string) while the live consumer's guard is `IsNullOrEmpty(...Trim())`; since `FormattedAddress` is a nullable column, a `NULL` row is retried by the live consumer but permanently skipped by the sweeper. `src/messaging/mytravels.messaging/AppendFormattedAddressSweeper.cs:33` vs. `AppendFormattedAddress.cs:43`.
- **F-12** — Race condition: `AppendFormattedAddress`'s per-class semaphore has no counterpart in `AppendFormattedAddressSweeper`, which runs concurrently in the same process with no locking and no optimistic-concurrency token on the entity — a POI can be geocoded twice under contention. `src/messaging/mytravels.messaging/AppendFormattedAddressSweeper.cs` (whole file) vs. `AppendFormattedAddress.cs:14`.
- **F-14** — `GOOGLE_API_KEY`/`GoogleApiKey` is wired through Docker Compose (`1-dockerize/docker-compose.yml:113`, `2-dockerhub/docker-compose.yml:105`) but never appears in any Kubernetes manifest (`3-kubernetes/manifests/**`, `4-argocd/manifests/**`) — deploying to k8s silently loses the configured Google Maps key and falls back to OpenStreetMap, unless this is intentional for the tutorial.
- **F-17 [RESOLVED — tools removed]** — Inconsistent error handling between the two MCP upload tools (`upload_photo` wrapped domain failures in `McpException`, `upload_photo_with_coordinates` did not). Both tools were deleted when `mcp` became read-only; `PointOfInterestMcpTools` now exposes only `search_pointofinterest`, which does guard its input.
- **F-19** — `SaveFileAsPointOfInsterestAsync` writes the image to MinIO **before** validating that it has GPS EXIF, and the `InvalidOperationException("Image is not geocoded")` guard leaves that object behind with no compensating delete. Every rejected upload orphans a blob in `uploaded-images` that nothing references and nothing reaps. `src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs:48-56`.
- **F-20** — The `-failed` exchanges have **no bound queue**, so a `FailedMessage` published after 3 retries is dropped by the broker on arrival (fanout with no bindings discards). The dead-letter path looks durable but only the `MessageAuditLogs` `Failed` row survives; the payload needed to replay the work is lost. `src/common/mytravels.common/Services/MessageSubscriberBase.cs` (failed-publish branch), `src/messaging/mytravels.messaging/FailedExchangeDeclarer.cs`. Compounding this, the exchanges are declared `autoDelete: true`, so they vanish whenever the last connection that declared them closes.
- **F-21** — `MessageSubscriberBase<T>`'s retry republishes **immediately, with no delay or backoff**, so a failure that needs time to clear (provider outage, rate limit, unavailable dependency) burns all 3 retries within milliseconds and dead-letters as fast as a permanent failure would. The 429-from-Anthropic case is the clearest instance: a rate limit is precisely the failure that retrying instantly cannot fix. `src/common/mytravels.common/Services/MessageSubscriberBase.cs` (retry branch).
- **F-22** — `AppendImageTags` is the only subscriber with no "already done" guard (contrast `FormattedAddress` non-empty and `ImageResized == true`), so any redelivery re-calls the metered Anthropic API and rewrites `Description`. A `Description is not null` check would make it idempotent like its siblings. `src/messaging/mytravels.messaging/AppendImageTags.cs:38-58`.
- **`cleanup-migrations`'s DELETE statement has never actually executed, in every Compose stage.** Its `command:` is given as a plain multi-line YAML string (`command: |`), which Compose itself shell-word-splits before handing anything to the container — only the first whitespace-delimited word ever reaches `entrypoint: ["sh", "-c"]`, so a command containing a heredoc (as this one does, `psql ... <<'EOSQL' ... EOSQL`) silently truncates to just that first word. Discovered while building the Flagsmith one-shot services (`1-dockerize/docker-compose.yml`), which avoid the same trap by giving `command` as a YAML **list** ending in the full script as one string element (`["sh", "-c", "<script>"]`) instead of a scalar block string. Not fixed here — flagged as a candidate for a separate fix. `0-local/docker-compose.yml`, `1-dockerize/docker-compose.yml`, `2-dockerhub/docker-compose.yml` (`cleanup-migrations` service, `command:` block).
- **F-18 [OPEN — credential still live in history]** — **All eight stage-3 Secret manifests are committed to the repository.** `.gitignore` contains no `secret` rule whatsoever (it does correctly ignore `.env`/`.env.*`, just not manifests), so nothing has ever excluded them. Seven hold only tutorial credentials; `3-kubernetes/manifests/messaging/1-secret.yaml` committed a real-format Azure Content Safety endpoint and 84-character key in `a270e7d` — base64-encoded, which is not protection. Removed from the tracked file in `2318b4d`, **but re-verified 2026-09-13: both values still return from `git show a270e7d:3-kubernetes/manifests/messaging/1-secret.yaml`.** The April 2026 `git filter-repo` scrub does not cover this — the commit postdates it by four months. Required: rotate at the provider, then scrub history again. The general problem (secrets committed rather than ignored) also remains; stage 4's out-of-band `kubectl create secret` approach is the fix stage 3 has not adopted. `.gitignore`, `3-kubernetes/manifests/*/1-secret.yaml`.

### Fragile or risky logic

- **F-4** — Swagger UI is mounted unconditionally in every environment (not gated by `IsDevelopment()`), and combined with zero authentication anywhere, the full API surface/schema is discoverable in any deployment of this image as-is. `src/api/mytravels.api/Program.cs:86-91`.
- **F-5** — No authentication/authorization anywhere in the API — every endpoint including image upload is fully anonymous; `CorsHosts` defaults to `*`. `src/api/mytravels.api/Program.cs` (no `UseAuthentication`/`UseAuthorization` calls); `appsettings.json:CorsHosts`.
- **F-7** — `UseHttpsRedirection()` is called unconditionally, but the container only exposes/binds HTTP (`ASPNETCORE_URLS=http://0.0.0.0:5101`) — the redirect middleware has no HTTPS port to target inside the container, effectively dead unless TLS termination happens upstream and the app is never actually asked to redirect. `src/api/mytravels.api/Program.cs:105`, `Dockerfile:27-29`.
- **F-9** — Every first-party .NET project targets `net10.0` but pins EF Core / Npgsql / Microsoft.Extensions.* packages to the `9.0.x` line — a consistent framework/package version skew across the whole solution. `mytravels.mcp` follows the same pattern (EF Core 9.0.9 / Npgsql 9.0.4 under `net10.0`). All `*.csproj` files under `src/`.
- **F-16 [RESOLVED — surface removed]** — The REST controller and the MCP tool layer used to validate `SaveCoordinatesDto` through two independently maintained code paths (`ModelState` vs. `Validator.TryValidateObject`). With MCP upload gone, no DTO is validated twice; `mcp`'s only check is a blank-`term` guard.
- **`mcp` still DI-registers four services it can no longer reach** — `IMessagePublisher`, `IMessageAuditLogger`, `IGeoService` and `IImageDescriptionService` remain registered in `mytravels.mcp/Program.cs` purely to satisfy `PointOfInterestService`'s constructor after the write tools were removed. Reading the registrations alone implies `mcp` can publish messages and call Anthropic; it cannot. `src/api/mytravels.mcp/Program.cs:52-60`.
- **`PointOfInterestMcpTools.ToDto` silently drops `CorrelationId`** even though `PointOfInterestDto` carries it and the REST path populates it. `Description` was added to the projection in `mcp:v1.0.2` alongside the SOLR work, so the gap is now one field rather than two. `src/api/mytravels.mcp/Tools/PointOfInterestMcpTools.cs` (`ToDto`).
- **Two unauthenticated write surfaces instead of one** — the MCP server duplicates the API's complete lack of auth (§11) while adding its own public ingress host, so hardening the REST API alone would no longer close the write path. `src/api/mytravels.mcp/Program.cs`, `*/manifests/ingress.yaml` (`mcp.mytravels.local`).
- **No OpenTelemetry sampling is configured** on any of the three instrumented services — `.UseOtlpExporter()` with default always-on sampling means trace volume scales 1:1 with request volume, and there is no `OTEL_TRACES_SAMPLER` handling to dial it back. Fine for a tutorial, a cost/throughput risk if copied into a real deployment. `src/api/mytravels.api/Program.cs:23-32`, `src/api/mytravels.mcp/Program.cs:22-32`, `src/messaging/mytravels.messaging/Program.cs:22-32`.
- **RabbitMQ queues still have no broker-level DLX, TTL, or max-length** — the retry bound is implemented in application code via an `x-retry-count` header on a republished copy (§12). This is weaker than a broker DLX in two ways: the counter is lost if the broker redelivers the *original* unacked message after a crash, and the "dead letter" goes to an exchange nothing is bound to (F-20). `src/common/mytravels.common/Services/MessageSubscriberBase.cs` (queue declaration, no `arguments`).
- **Audit-log writes open a fresh DI scope and a separate `SaveChangesAsync` per event**, on the hot path of every publish and every consume — up to 6+ extra round-trips per uploaded photo, in a table with no retention policy or pruning job. `MessageAuditLogs` grows unboundedly for the life of the database. `src/common/mytravels.common/Services/MessageSubscriberBase.cs` (`LogAuditEventAsync`), `MessagePublisher.cs`, `domain/Features/Traceability/MessageAuditLogger.cs`.
- **`GetCorrelationSummariesAsync` groups and sorts the entire `MessageAuditLogs` table on every page request** — `GroupBy(CorrelationId)` + `OrderByDescending(LastEventAt)` before `Skip`/`Take`, with no index beyond the correlation-id one and no date bound. Fine at tutorial volume, quadratically painful as the audit table grows. `src/common/mytravels.domain/CoreDbContext.cs:72-88`.
- **Both `MessagePublisher` and `MessageSubscriberBase<T>` reach `CorrelationId` and `PointOfInterestId` by reflection** (`GetType().GetProperty("CorrelationId")?.GetValue(...) as Guid?`) — despite `IMessage` declaring `CorrelationId` as a real member and `MessageSubscriberBase<T>` being constrained to `where T : IMessage`, so `message.CorrelationId` would compile and be checked. `PointOfInterestId` genuinely is not on any interface, so a message type that omits or renames it silently loses that half of its audit trail with no compile error. `src/common/mytravels.common/Services/MessagePublisher.cs`, `MessageSubscriberBase.cs`.
- **`MessagePublisher` opens a new AMQP connection+channel per publish call** — no pooling, a throughput/latency risk under load. `src/common/mytravels.common/Services/MessagePublisher.cs:19-37`.
- **`AppendFormattedAddressSweeper` does a full unfiltered `SELECT *` on `PointOfInterests` every 30 minutes**, filtering in memory — scales poorly as the table grows. `src/common/mytravels.domain/CoreDbContext.cs:32-33` (`GetPointsOfInterestAsync`) via `AppendFormattedAddressSweeper.cs:31`.
- **`CoreDbContext.UpdateAddressAsync` loads the entire `PointOfInterests` table into memory** and filters by key in C# rather than querying by key directly, despite the context being globally `NoTracking` (requiring manual `EntityState`/`IsModified` toggling to make the update stick). `src/common/mytravels.domain/CoreDbContext.cs:85-102`.
- **MinIO image is pulled unpinned (`quay.io/minio/minio`, no tag)** in every Compose file — reproducibility risk. `0-local/docker-compose.yml`, `1-dockerize/docker-compose.yml`, etc. (minio service `image:` line).
- **Migration/`db-migrations` Job has no resource requests/limits** set, unlike every other Deployment in the same manifest set. `3-kubernetes/manifests/migrations/2-job.yaml`, `4-argocd/manifests/migrations/job.yaml`.
- **No `readinessProbe` on postgres/rabbitmq/minio/api Deployments** (only `livenessProbe`); messaging's `livenessProbe` is commented out entirely. `3-kubernetes/manifests/{postgres,rabbitmq,minio,api,messaging}/*deployment.yaml`.
- **Orphan `rabbitmq-config-pvc`** created but never mounted by the RabbitMQ Deployment in stage 3 (fixed in stage 4 per commit `4ced81d`, but the stage-3 manifest was never updated to match). `3-kubernetes/manifests/rabbitmq/3-pvc.yaml` vs. `4-deployment.yaml`.
- **F-15 [RESOLVED]** — `3-kubernetes/manifests/messaging/1-secret.yaml` provisioned `ContentSafetyEndpoint`/`ContentSafetyKey` for an Azure Content Safety integration that no code in `src/messaging` ever referenced — dead config for a feature that isn't (or was never) implemented. These keys (and the matching env entries in `2-deployment.yaml`/`4-argocd/manifests/messaging/deployment.yaml`, `.env.example`, and both runbooks) have been removed. The credential is still recoverable from git history — see F-18.
- **`mytravels.migration.csproj` targets `net10.0` but its EF Core/Hosting/Npgsql packages and pinned `dotnet-ef` CLI tool are all on the `9.0.x` line** — consistent with F-9 but worth flagging separately since it directly affects the migration-bundle build. `src/common/mytravels.migration/mytravels.migration.csproj`, `src/.config/dotnet-tools.json`.
- **cAdvisor v0.49.1 inotify file descriptor limit [RESOLVED]** — cAdvisor requires kernel file descriptor limits (`fs.file-max` and `fs.nr_open`) set to at least 2097152 to initialize inotify for filesystem monitoring. This was fixed by adding init containers to the cAdvisor DaemonSet in stages 3–4 that run `sysctl` to raise these limits before cAdvisor starts. `3-kubernetes/manifests/observability/14-cadvisor-daemonset.yaml`, `4-argocd/manifests/observability/cadvisor-daemonset.yaml`.

### Dead code / ambiguous logic / undocumented behaviour

- **F-1** — `AzureStorageService` is fully implemented but never registered in any DI container, and its required config key (`StorageAccountConnectionString`) exists in no `appsettings.json` in the repo — dead code with a broken namespace (`mytravels.common.Services` inside the `mytravels.storage` project, unlike the correctly-namespaced `MinIOStorageService`). `src/common/mytravels.storage/AzureStorageService.cs:12,25`.
- **F-3** — `mytravels.migration`'s `Program.cs` registers a DbContext and calls `host.RunAsync()` with no hosted services and no `Database.Migrate()` call anywhere — it never applies migrations itself. The real runtime artifact is a separately built `efbundle` executable (`dotnet ef migrations bundle`), set as the container `ENTRYPOINT`. A reader following only `Program.cs` would be misled about how migrations actually run. `src/common/mytravels.migration/Program.cs:8-23`, `Dockerfile:21-26,48`.
- **F-10** — `Hosted/` directory in `mytravels.api` exists but is completely empty; `Services/` and `Profiles/` directories in `mytravels.messaging` exist but are empty and not even git-tracked; `mytravels.common/Config/`, `Models/`, `Policies/` and `mytravels.contract/Attributes/` are likewise empty placeholders with no tracked files — the shared-library layout implies more structure than currently exists (Polly policies, for instance, are defined inline in the maps services rather than in the empty `Policies/` folder).
- **F-13 [RESOLVED, with a new edge — see F-19]** — messages were previously published even when EXIF yielded `(0,0)`, so geocoding was attempted against "null island". `SaveFileAsPointOfInsterestAsync` now rejects such uploads outright, so no `(0,0)` row is created and no message is published for one. The guard's `||` (rather than `&&`) also rejects legitimate on-axis coordinates, and the rejection leaves an orphaned blob (F-19). `src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs:48-56`.
- **The frontend's `hasCoordinates` helper is now vestigial** — it exists to filter out `(0,0)` rows that the upload path no longer creates. Harmless, but misleading about what states the data can be in. `src/web/src/api/types.ts` (`hasCoordinates`).
- **`SeedData` migration (`20600930075821_SeedData.cs`) and `UpdateStoredProcedure` migration (`20590930075747_...`) carry migration timestamps decades in the future** (2059, 2060) relative to `Init`'s 2026 timestamp — deliberate, not bogus: it guarantees these two always sort after any normal-dated schema migration added later, and both stages' migration Jobs (`3-kubernetes/manifests/migrations/2-job.yaml`, `4-argocd/manifests/migrations/job.yaml`) plus every Compose stage delete these two migrations' `EFMigrationsHistory` rows before each run specifically so they always re-execute (idempotent `CREATE OR REPLACE FUNCTION` scripts) even though EF normally applies a migration only once. Both also have non-reversible `Down()` methods (`NotSupportedException`/`NotImplementedException`). As of the 2026-09-12 migration squash (7 → 3: `Init`, `UpdateStoredProcedure`, `SeedData`), these two IDs were intentionally kept unchanged so the hardcoded `DELETE ... WHERE "MigrationId" IN (...)` statements in all 5 stages didn't need editing. A fourth migration, `20610930080000_AddCorrelationIdToPointOfInterestFunctions`, was added after the squash and follows the same convention — a 2061 timestamp, so it sorts after `SeedData` and re-applies the `CREATE OR REPLACE FUNCTION` scripts carrying the new `Description`/`CorrelationId` columns. Note it is **not** in the stages' `DELETE` lists, so unlike its two siblings it runs exactly once. A fifth, `20620930080000_DropPointOfInterestByTagNameFunction`, follows the same convention with a 2061-beating 2062 timestamp so its `DROP FUNCTION` lands *after* `AddCorrelationIdToPointOfInterestFunctions` replays the remaining `.sql` files; it is likewise absent from the `DELETE` lists and runs once. `src/common/mytravels.domain/Migrations/20590930075747_UpdateStoredProcedure.cs`, `20600930075821_SeedData.cs`, `20610930080000_AddCorrelationIdToPointOfInterestFunctions.cs`, `20620930080000_DropPointOfInterestByTagNameFunction.cs`.
- **`SeedData` migration references a `SeedScripts/` folder that does not exist anywhere in source** (only stale copies remain in `bin/Debug` build output) — the migration silently no-ops today. `src/common/mytravels.domain/Migrations/20600930075821_SeedData.cs:14-15`.
- **Misspelled/typo identifier preserved through the codebase**: `IPointOfInterestService.SaveFileAsPointOfInsterestAsync` (should be "Interest") — load-bearing (renaming requires touching calling code across the solution). `src/common/mytravels.contract/Interfaces/IPointOfInterestService.cs:11-12`. (The sibling `PointOfInterestAuditLog.Sucessful` misspelling this entry used to note no longer exists — that table was replaced by `MessageAuditLogs`, see §8.1.)
- **Commented-out dead attribute** `//[Index(IsUnique = true)]` on `Tag.Name` — the actual uniqueness constraint is enforced separately via fluent API in `CoreDbContext.OnModelCreating`, making the comment misleading if read in isolation. `src/common/mytravels.contract/Entities/Tag.cs:13`.
- **`ApiException`'s serialization constructor unconditionally throws `NotImplementedException`** — a dead/broken deserialization path (only matters if this exception type is ever cross-AppDomain/remoted, unlikely in this architecture but technically broken). `src/common/mytravels.contract/CustomException/ApiException.cs:23-26`.
- **Copy-pasted MinIO SDK example code left in the production path**: `Console.WriteLine("Running example for API: ...")` calls and an upstream example-repo comment inside `ListObjectsAsync`/`RemoveObjectAsync`, both of which also swallow exceptions via generic `catch (Exception e) { Console.WriteLine(...); }` instead of using the app's `ILogger`. `src/common/mytravels.storage/MinIOStorageService.cs:240,256,268-271,307,310-313`.
- **`GooglePlacesUrl` config key is declared but never read** anywhere in `GoogleMapsService.cs`. `src/api/mytravels.api/appsettings.json:12`, `src/messaging/mytravels.messaging/appsettings.json:12`.
- **`GRAPH_API_TOKEN` env var is declared in `.env.example` for stages 0–3 but never consumed** by any Compose service or k8s manifest — dead variable, dropped in stage 4.
- **Misleading `launchSettings.json` profile name**: the messaging project's only launch profile is named `mytravels.api`, evidently copy-pasted from the API project. `src/messaging/mytravels.messaging/Properties/launchSettings.json:3`.
- **`mytravels.api.http` sample file references `GET /jobs/`**, a route that exists in neither controller — stale sample request. `src/api/mytravels.api/mytravels.api.http:3`.
- **`mytravels.common.csproj` excludes a self-named nested `mytravels.common\**` path that doesn't exist** — vestigial template leftover. `src/common/mytravels.common/mytravels.common.csproj:10-12`.
- **Stored procedure filename/function-name mismatch**: file `spGetPointOfInterests.sql` (plural) defines function `public.spGetPointOfInterest()` (singular) — confusing but not a runtime bug since callers use the correct singular name. `src/common/mytravels.domain/Features/PointOfInterest/spGetPointOfInterests.sql:1`.
- **`ingress.yaml` header comments (both stage 3 and stage 4) omit `messaging-ingress`** from their descriptive comment even though the manifest body defines it — documentation/body mismatch. (`solr` was added to both the header and the body together when SOLR landed, so it does not repeat the mismatch.) `3-kubernetes/manifests/9-ingress.yaml:1-12,67-86`; `4-argocd/manifests/ingress.yaml:1-12,69-89`.
- **`scripts/init-dbs.sql` is a no-op** — `CoreDb` is already created via `POSTGRES_DB`, so the script exists only as an unused hook.
- **Web frontend's `README.md` is the unmodified Vite template README** — not project-specific documentation. `src/web/README.md`.
- **`tsconfig.app.json`/`tsconfig.node.json` do not visibly set `strict: true`**, unusual for a current Vite/React template default — worth confirming there's no extended base config, since none was found in the directory listing.
- **The POI list is silently truncated at `rows` (default 100), and the map is the thing that truncates.** `GET /api/pointofinterest` is a single capped SOLR query with no server-side paging loop and no PostgreSQL fallback, so a library of more than 100 points draws an incomplete map with no indication that anything is missing — and the web app never passes `rows`, so it always takes the default. Accepted deliberately when the list moved onto SOLR; widening it means passing `?rows=` or paging the caller. `src/api/mytravels.api/Controllers/PointOfInterestController.cs` (`GetMetadatasAsync`), `src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs` (`GetAsync`).
- **A newly uploaded POI is absent from the map until the `index-solr` message is consumed.** With the list served from PostgreSQL the point appeared the instant the upload returned; it now waits on RabbitMQ and the messaging worker. `MapPage`'s post-upload poll (ten attempts, three seconds apart) covers the normal case, but a worker that is down or backed up leaves the point invisible until the next page load, with no error shown. Accepted deliberately alongside the item above. `src/web/src/components/MapPage.tsx` (`pollUntilResolved`).
- **`tag_exact` is written on every SOLR document but read by nothing.** It existed to serve `GET /api/pointofinterest/filter`, which was deleted in `api:v1.0.12`; `SolrSearchService` queries `formatted_address`, `tags` and `description` only. The field is kept on purpose — dropping it would make reintroducing an exact-tag filter a reindex rather than a query change — but it is dead weight in every document until something reads it again. `src/common/mytravels.common/Services/SolrIndexService.cs` (`Fields`, `ToDocument`).
- **`tags`, `tag_exact` and `tag_ids` are three parallel multiValued SOLR fields whose correctness depends on positional ordering.** `SolrSearchService` pairs a tag name with its PostgreSQL id by index, which is required because both `ToDto` helpers discard tags with a null `TagId`. SOLR does preserve the submitted order of a multiValued field, so this holds — but it is an ordering contract enforced by nothing, and a future writer that populates the three fields separately would corrupt the pairing silently. `src/common/mytravels.common/Services/SolrIndexService.cs` (`ToDocument`), `SolrSearchService.cs` (`Expand`).
- **PV/nodeAffinity for MinIO is hard-pinned to a specific k3d node name** (`k3d-mytravels-agent-2`) in both k8s manifest stages — reasonable for a `hostPath`-backed tutorial cluster, but a portability constraint baked into application manifests rather than infra config.
- **All five service tags are aligned across every stage** (`migrations:v1.0.8`, `api:v1.0.12`, `messaging:v1.0.14`, `mcp:v1.0.2`, `web:v1.0.8`). ⚠️ **`api:v1.0.12` and `web:v1.0.8` were bumped by the map-search work and are NOT yet published** — neither is on Docker Hub. Stages 2–4 will fail to pull until both architectures are built and `merge-manifests.sh` has run (see the next item). The other three (`migrations:v1.0.8`, `messaging:v1.0.14`, `mcp:v1.0.2`) are published and verified (Docker Hub, 2026-09-14). `api` was additionally *out of lockstep* before this change — `1-dockerize/docker-compose.yml`, `4-argocd/manifests/api/deployment.yaml` and `merge-manifests.sh` already read `v1.0.12` while the authoritative `2-dockerhub/docker-compose.build.yml`, `2-dockerhub/docker-compose.yml` and `3-kubernetes/manifests/api/2-deployment.yaml` still read `v1.0.11`; the bump to `v1.0.12` everywhere closed that drift as well. `2-dockerhub`'s `web` was previously stuck two tags behind (`v1.0.5`, then briefly `v1.0.6`) because bumping it to a published multi-arch, sentinel-substitutable tag required the deploy-time `render-web-config` Compose step added alongside this fix (§10.5), unlike the other four services which are plain tag bumps. Stage 1's local `web` build tag is cosmetic (it always builds from current source), so it was bumped to match for consistency only.
- **A tag bump is not a publish, and the gap has bitten this repo before** — `docker-compose.build.yml` pushes per-architecture tags (`…-arm64` from macOS, `…-amd64` elsewhere) and `src/scripts/merge-manifests.sh` merges them into the plain tag the manifests reference. A bump committed without both builds leaves stages 2–4 pulling a tag that does not exist, which surfaces as `ImagePullBackOff` on the `db-migrations` Job rather than as an obvious build error. `merge-manifests.sh` hardcodes the same five tags, so it is a sixth place to keep in sync. `2-dockerhub/docker-compose.build.yml`, `src/scripts/merge-manifests.sh`.
- **`1-dockerize/tools/` contains only a gitignored `node_modules/`** (tsx, typescript, json-schema-to-ts) with no tracked source file — a leftover scaffold directory that reads as meaningful content until opened.

---

## 18. Glossary

| Term | Meaning |
|---|---|
| **POI** | Point of Interest — the core domain entity: a geotagged photo with optional address/tags |
| **Null island** | Geodetic slang (used here as an inferred description, not a code identifier) for coordinate `(0,0)`, overloaded in this codebase as the "unresolved location" sentinel |
| **Fanout exchange** | RabbitMQ exchange type that broadcasts to every bound queue, ignoring routing key — used for all ten of this app's exchanges |
| **Correlation ID** | A `Guid` minted per POI at creation, stored on the row and stamped on every message about it; the join key for `MessageAuditLogs` and the unit the traceability UI groups by. Distinct from an OpenTelemetry trace id, which is propagated separately via the `traceparent` header |
| **Failed exchange** | The `<name>-failed` fanout twin of each work exchange, receiving a `FailedMessage` after 3 retries. Non-durable, auto-delete, and unbound — publishing to it records the attempt in audit logs but discards the payload (F-20) |
| **Audit event** | One `MessageAuditLogs` row: `Published`, `ConsumeSucceeded`, `Retried`, or `Failed`, written best-effort around each publish/consume |
| **Sweeper** | The `AppendFormattedAddressSweeper` cron-style background job that retries failed address resolutions on a timer |
| **SOLR collection** | `mytravels-pois`, the single Apache SOLR index that backs POI search. A derived store, keyed on `PointOfInterestKey`, kept current by the `index-solr` exchange and rebuildable from PostgreSQL via `reindex-solr` |
| **edismax** | SOLR's Extended DisMax query parser, used here with query-time field boosts (`formatted_address^5 tags^3 description^1`) so relevance can be retuned without a reindex |
| **Reindex** | A full purge-and-rebuild of the SOLR collection from `spGetPointOfInterest()`, triggered by `POST /api/pointofinterest/reindex` and executed in `messaging`; the recovery path for a lost or diverged index |
| **efbundle** | A self-contained native executable produced by `dotnet ef migrations bundle`, used as the migration container's actual entrypoint (distinct from `mytravels.migration`'s `Program.cs`) |
| **Sync wave** | Argo CD annotation (`argocd.argoproj.io/sync-wave`) controlling the order in which manifests are applied during a GitOps sync |
| **Self-heal** | Argo CD feature (`syncPolicy.automated.selfHeal`) that automatically reverts manual cluster drift back to the Git-defined state; deliberately introduced as a separate lesson step in stage 4 rather than enabled from the start |
| **MCP** | Model Context Protocol — the tool-calling protocol `mytravels.mcp` speaks, letting an LLM client search saved points of interest and look up places without going through the REST API (read-only; it cannot create or modify anything) |
| **MCP tool** | A named, described, schema-typed function an MCP server exposes (here: `search_pointofinterest`, `search_place`); declared with `[McpServerTool]` + `[Description]` attributes |
| **OTLP** | OpenTelemetry Protocol — the wire format api/mcp/messaging use to ship traces and metrics to the collector (gRPC on 4317; OTLP/HTTP for browser RUM) |
| **RUM** | Real User Monitoring — browser-side tracing in `src/web`, capturing document load and `fetch` spans and exporting them to the collector |
| **Tempo** | Grafana's trace backend; the collector's trace sink, queried through Grafana alongside Prometheus metrics |
| **k3d** | A tool for running lightweight k3s Kubernetes clusters inside Docker, used as the tutorial's local cluster |
| **CKAD / CKA / CKS / KCNA** | CNCF/Linux Foundation Kubernetes certifications referenced in `CERTIFICATION.md` as the tutorial's target learning outcomes |

---

*End of specification. 18/18 sections present, none marked N/A. Findings: 22 lettered (F-1…F-22, of which F-13, F-15, F-16 and F-17 are marked resolved) plus additional unlettered items in the "fragile" and "dead code" sub-buckets — 40+ total findings.*

*Revised 2026-09-14 (feature flagging via self-hosted Flagsmith, per `prompts/add feature flagging.md`):*
- *§1, §14 — Flagsmith added as an integration: shares `mytravels-postgres` in a second database (`FeatureDb`), evaluated from `api` and `messaging` via the OpenFeature .NET SDK (`OpenFeature.Contrib.Providers.Flagsmith` v0.3.1) and from `web` via `@openfeature/web-sdk`/`@openfeature/react-sdk`/`@openfeature/flagsmith-client-provider`; `mcp` is not wired to it.*
- *§6.1 — `/api/pointofinterest/search` and both `/api/Traceability` actions now 404 when their respective flag is off: `enable-poi-search` and `enable-message-tracing`.*
- *§7.1, §7.5 — `AppendImageTags` gates the Anthropic call and `Description`/tag persistence behind `enable-image-description`; the trailing `index-solr` publish is unconditional either way.*
- *§10.1, §10.2 — new `Flagsmith:ApiUri`/`Flagsmith:ServerSideEnvironmentKey` config section on `api` and `messaging`, same environment key value on both.*
- *§17 — new finding: Compose's `command:` field, given as a plain multi-line string, is shell-word-split by Compose itself, so only the first word reaches `sh -c` — a pipe/`||`/heredoc silently never executes. This affects the pre-existing `cleanup-migrations` service in every Compose stage, whose DELETE statement has never actually run; discovered while building the new Flagsmith one-shot services, which avoid it by giving `command` as a YAML list instead. Not fixed as part of this revision.*
- *Image tags bumped: `messaging:v1.0.16`, `api:v1.0.13`, `web:v1.0.9` — new Flagsmith dependency (`api`/`messaging`) and a fourth web config sentinel (`web`); not built, not pushed.*

*Revised 2026-08-23: added `mytravels.mcp` and the OpenTelemetry/Prometheus/Tempo/Grafana observability stack, which the original spec predated (§1–§6, §10, §14, §15, §18). Corrected §11's secrets claims — the asserted `*secret.yaml` gitignore rule does not exist and stage-3 secrets are committed, including a live-format Azure credential (F-18). Corrected the `web` image skew from v1.0.5 to v1.0.6.*

*Revised 2026-09-13: `2-dockerhub`'s pinned `mytravels-web` tag (`v1.0.5`, arm64-only manifest on Docker Hub) was failing `docker compose pull` on amd64 hosts. Fixed by aligning `web` to `v1.0.7` everywhere and adding a `render-web-config` Compose service that replicates the k8s `render-config` init container's sentinel substitution, since `v1.0.7`'s image requires it (§10.5, §16). All five service tags are now consistent across every stage.*

*Revised 2026-09-14 (map search box, per `prompts/add a search box.md`):*
- *§6.1 — `GET /api/pointofinterest` is now served from SOLR as a single capped `*:*` query and takes `rows`/`start`; `GET /api/pointofinterest/filter` and the controller overload behind it are **deleted**. `GetAllPointsOfInterestAsync` stays on `ICoreDbContext` because the reindex rebuild still reads it.*
- *§6.2 — corrected a stale description: `search_pointofinterest` takes `term` only. `SolrSearchQuery` has carried no `Tag`, `From` or `To` since the search simplification, so the `tag`/`from`/`to` arguments this spec listed never existed on the shipped tool.*
- *§9 — `tag_exact` is documented as written-but-unread now that `/filter` is gone; it is kept on purpose so an exact-tag filter can come back as a query change rather than a reindex. The read-your-writes note is rewritten: with the list on SOLR there is no PostgreSQL read path left to hide the indexing lag.*
- *§17 — the `/filter` semantics finding is retired with the endpoint; two consequences of the SOLR-backed list replace it (silent truncation at `rows`, and a POI being absent from the map until `index-solr` is consumed). Both were accepted deliberately.*
- *Web: a floating search control on the map (`MapSearchBox`), debounced at 500 ms, firing at 4+ characters and aborting the in-flight request per keystroke. `FitToMarkers` now refits on which points are displayed rather than on array identity, so the three-second upload poll no longer yanks the viewport; `MapPage` keeps the full library and the search results apart so the post-upload poll still counts against the full list.*
- *Image tags bumped: `api:v1.0.12`, `web:v1.0.8` — not built, not pushed.*

*Revised 2026-09-13 (third pass — SOLR-backed POI search, per `prompts/todo/implement search.md`):*
- *§2, §6.4, §18 — four new exchanges (`index-solr`, `reindex-solr` and their `-failed` twins); the message-flow diagram gains the indexing chain and the rebuild path.*
- *§6.1 — `search` gains `tag`/`from`/`to`/`rows`/`start`; `filter` now means an exact tag match; new `POST /api/pointofinterest/reindex` returning 202.*
- *§6.2 — `search_pointofinterest` widened to `tag`/`from`/`to`, and its projection now includes `Description`.*
- *§6.5 — both PostgreSQL search methods removed from `ICoreDbContext`; new §6.5b for `ISolrSearchService`/`ISolrIndexService`.*
- *§8.3 — `spGetPointOfInterestByTagName` dropped, with migration `20620930080000_DropPointOfInterestByTagNameFunction`.*
- *§9 — SOLR documented as a second, derived persistence store: key choice, idempotent indexing, runtime schema, and the absence of read-your-writes across the two stores.*
- *§4, §5, §10, §11, §14, §15 — SOLR added to the stack table, module responsibilities, the `Solr` config section, the unauthenticated-services list, integrations, and the probe notes.*
- *§10.1 — the stale `AnthropicApiKey` placeholder was removed from `mytravels.api/appsettings.json`.*
- *Image tags bumped: `migrations:v1.0.8`, `api:v1.0.11`, `messaging:v1.0.14`, `mcp:v1.0.2`; `web` unchanged at `v1.0.7`.*
- *Two items this spec previously flagged as stale were already correct and needed no change: §7.5 (descriptions are persisted) and §3 (no `3-kubernetes/docker-compose.yml`).*

*Revised 2026-09-13 (second pass — resynchronised against source after the traceability and async-tagging work):*
- *§1, §2, §5, §6.4 — added the `append-image-tags` exchange and the three `-failed` exchanges; corrected the §1 topology diagram, which routed Anthropic through `api`/`mcp` when only `messaging` calls it.*
- *§6.1 — removed `POST /api/pointofinterest/{id}/describe` (deleted); added `GET /api/pointofinterest/search`, `GET /api/traceability`, `GET /api/traceability/{correlationId}`; noted `ApiErrorDto.Id` is now the OTel trace id.*
- *§6.2, §2, §17 — `mcp` is read-only; the `upload_photo*` tools are gone, retiring F-16 and F-17.*
- *§7.1, §7.2, §15, §16, §17 — uploads without GPS EXIF now throw instead of creating a `(0,0)` row; F-13 resolved, new F-19 (orphaned blob on rejection) raised.*
- *§7.5 rewritten: image description/tagging is asynchronous and **persisted** (`PointOfInterest.Description` + tags), not an on-demand display-only endpoint. New §7.6 covers message traceability end to end.*
- *§8.1, §8.3 — added `Description` and `CorrelationId` columns and the `20610930080000` stored-procedure migration.*
- *§10.1, §10.2, §10.6 — Anthropic config moved from `api` to `messaging`; `AnthropicModel` is now deployment-configurable.*
- *§12, §13, §14 — replaced the "infinite `nack(requeue:true)` redelivery" description with the actual bounded 3-retry + failed-exchange policy; added `AppendImageTags` and `FailedExchangeDeclarer` to the hosted-service list. New findings F-20 (failed exchanges have no bound queue, so payloads are discarded), F-21 (retries have no backoff), F-22 (`AppendImageTags` is not idempotent).*
- *§11 — verified the Azure Content Safety credential is **still present** in commit `a270e7d`; it was committed after the April 2026 `filter-repo` scrub and has only been removed from the working tree, not from history.*
- *§3 — removed the non-existent `3-kubernetes/docker-compose.yml` (Compose stops at stage 2); added `prompts/todo/`, `user stories/`, `.claude/`.*
- *§17 — confirmed all five image tags are published on Docker Hub, and documented that a tag bump alone does not publish (per-arch build + `merge-manifests.sh` merge required).*
