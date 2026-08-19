# MyTravels / intro-to-k8s — Technical Specification

**Repository:** `k8sforum/intro-to-k8s`
**Generated:** 2026-08-19, reverse-engineered from source (`devops/` excluded per instruction — none exists in this repo).

This document specifies two things that are inseparable in this repo:

1. **MyTravels** — a .NET 10 / React geolocation Points-of-Interest (POI) application (source in `src/`).
2. **A five-stage Kubernetes deployment tutorial** that deploys the same application with progressively more sophisticated tooling (`0-local/` → `4-argocd/`), each stage a self-contained lesson with its own `docker-compose.yml`/manifests, `.env.example`, and a Jupyter `runbook.ipynb` that walks through it hands-on.

Throughout, **Observed:** marks behavior read directly from code/config; **Inferred:** marks reasonable interpretation not directly stated.

---

## 1. Overview

**MyTravels** lets a user upload a geotagged photo (or pick a location manually) to create a "Point of Interest" pin on a map. GPS coordinates are read from the photo's EXIF data on upload; if absent, the user searches for and picks a place instead. A background worker asynchronously (a) resizes the uploaded image into a thumbnail and (b) resolves a human-readable address for the coordinates via a maps geocoding API. The frontend is a single-page map view (Leaflet) that polls for updates while async resolution completes.

**Top-level components:**

| Component | Role | Source |
|---|---|---|
| `mytravels.api` | REST API — CRUD for POIs, image upload, place search | `src/api/mytravels.api/` |
| `mytravels.messaging` | Background worker — image resize, address resolution, retry sweep | `src/messaging/mytravels.messaging/` |
| `mytravels.migration` | One-shot EF Core migration bundle runner | `src/common/mytravels.migration/` |
| `web` | React SPA — map UI, upload flow | `src/web/` |
| `mytravels.common` / `mytravels.contract` / `mytravels.domain` / `mytravels.storage` | Shared libraries (DTOs, entities, EF Core context, geo/maps services, object storage) | `src/common/` |

**Runtime topology** (Observed, from `1-dockerize/docker-compose.yml` and `3-kubernetes/manifests/`):

```
                    ┌──────────────┐
   browser ───────► │  web (nginx) │
                    └──────┬───────┘
                           │ REST (baked-in base URL)
                           ▼
                    ┌──────────────┐        ┌───────────────┐
                    │ api (5101)   │◄──────►│  PostgreSQL   │
                    └──────┬───────┘        │  (5432)       │
                           │ publish             ▲
                           ▼                      │
                    ┌──────────────┐              │
                    │  RabbitMQ    │              │
                    │ (5672/15672) │              │
                    └──────┬───────┘              │
                           │ consume                │
                           ▼                      │
                    ┌──────────────┐              │
                    │ messaging    │──────────────┘
                    │ (5102)       │
                    └──────┬───────┘
                           │
                           ▼
                    ┌──────────────┐
                    │  MinIO (S3)  │
                    │ (9000/9090)  │
                    └──────────────┘
```

Five deployment stages progressively wrap this same topology:

| Stage | Directory | Adds |
|---|---|---|
| 0 | `0-local/` | Infra (Postgres/RabbitMQ/MinIO) via Compose; app run from source (`dotnet run` / `npm run dev`) |
| 1 | `1-dockerize/` | Full stack containerized, built locally via Compose |
| 2 | `2-dockerhub/` | Images built & pushed to Docker Hub, stack runs from registry images |
| 3 | `3-kubernetes/` | Deployed to a k3d Kubernetes cluster via raw `kubectl apply` manifests + Traefik ingress |
| 4 | `4-argocd/` | Same manifests, GitOps-deployed via Argo CD (Application/AppProject, sync waves, drift/self-heal) |

**Inferred:** the repo's primary purpose is pedagogical (CKAD/CKA-oriented — see `CERTIFICATION.md`), using a realistic multi-service app as the running example rather than toy manifests.

---

## 2. Architecture & Component Relationships

**Dependency graph** (Observed, `src/mytravels.sln` solution folders + `ProjectReference`s):

```
mytravels.api ────────────────┐
mytravels.messaging ──────────┼──► mytravels.common ──► mytravels.contract
                               ├──► mytravels.domain ──► mytravels.contract
                               └──► mytravels.storage ─► mytravels.contract
mytravels.migration ───────────────► mytravels.domain (design-time only)
web (React, separate npm project, no dependency on any C# project)
```

**Message flow** (Observed, `src/common/mytravels.contract/Constants/ExchangeNames.cs`, `MessageSubscriberBase.cs`, consumer files):

```
PointOfInterestService.CreatePointOfInterestAsync
        │
        ├──publish──► fanout exchange "append-formatted-address" ──► queue "append-formatted-address"
        │                                                                     │
        │                                                          AppendFormattedAddress (HostedService)
        │                                                                     │
        │                                                          IMapsService.GetAddressAsync (Google or OSM)
        │                                                                     │
        │                                                          UPDATE PointOfInterests.FormattedAddress
        │
        └──publish──► fanout exchange "resize-image" ──► queue "resize-image"
                                                                    │
                                                          ResizeImage (HostedService)
                                                                    │
                                                          MinIO: uploaded-images ──► resize 10% ──► resized-images
                                                                    │
                                                          UPDATE PointOfInterests.ImageResized = true

AppendFormattedAddressSweeper (CronJobBase, every 30 min)
        └── full-table scan PointOfInterests, retry rows created in last 2 days with empty FormattedAddress
```

Both exchanges are RabbitMQ **fanout**, bound with an empty routing key, and each publish/consumer pair opens/declares the exchange independently (no shared topology-setup step) — see §17 for what this implies at scale.

**Request flow (API)**: browser → `web` (nginx, static SPA) → directly to `api`'s public URL baked into the SPA bundle at Docker build time (no reverse proxy through `web`) → `CoreDbContext` (EF Core / Npgsql) → PostgreSQL. Image bytes are stored in MinIO, not the database; the DB holds only the blob name/container reference.

---

## 3. Repository & Folder Structure

```
intro-to-k8s/
├── 0-local/                     # Stage 0: infra via Compose, app run from source
│   ├── docker-compose.yml       # postgres, rabbitmq, minio, migration job
│   ├── runbook.ipynb            # hands-on lesson notebook
│   └── scripts/migrations.ps1
├── 1-dockerize/                 # Stage 1: full stack containerized (local build)
│   ├── docker-compose.yml
│   └── runbook.ipynb
├── 2-dockerhub/                 # Stage 2: images built & pushed to Docker Hub
│   ├── docker-compose.yml       # pulls from registry
│   ├── docker-compose.build.yml # multi-arch build/push helper
│   └── runbook.ipynb
├── 3-kubernetes/                # Stage 3: raw kubectl-applied manifests + Traefik ingress
│   ├── docker-compose.yml       # infra-only, no `web` service
│   ├── manifests/               # api/, messaging/, migrations/, minio/, postgres/, rabbitmq/, web/
│   ├── roadmap.md
│   └── runbook.ipynb
├── 4-argocd/                    # Stage 4: GitOps deploy of the stage-3 manifests via Argo CD
│   ├── argocd/                  # Application, AppProject, accounts, ingress, server-params
│   ├── cluster/traefik-config.yaml
│   ├── manifests/                # same shape as 3-kubernetes/manifests, secrets removed, sync-wave annotations added
│   └── runbook.ipynb
├── src/                          # Application source (the actual MyTravels app)
│   ├── api/mytravels.api/        # ASP.NET Core REST API (net10.0)
│   ├── messaging/mytravels.messaging/  # Background worker (net10.0, Web SDK)
│   ├── common/
│   │   ├── mytravels.common/     # Shared services: geo, maps, RabbitMQ pub/sub, cron base classes
│   │   ├── mytravels.contract/   # Entities, DTOs, interfaces, constants, custom exceptions
│   │   ├── mytravels.domain/     # EF Core DbContext, migrations, stored-proc SQL
│   │   ├── mytravels.storage/    # MinIO / Azure Blob storage adapters
│   │   └── mytravels.migration/  # EF Core migrations-bundle build project
│   ├── web/                      # React 19 + Vite + TypeScript + Tailwind v4 SPA
│   └── scripts/                  # DB grant script, manifest merge script
├── scripts/init-dbs.sql          # Postgres first-run hook (currently a no-op)
├── drawio/architecture.drawio    # Architecture diagram source
├── prompts/scaffold react app.md # Original prompt used to scaffold the web frontend
├── CERTIFICATION.md              # CNCF/Linux Foundation cert-path reference notes
├── AGENTS.md                     # Agent instruction: never refactor bin/obj/Migrations
└── 0-vs code extensions.md, 1-install tools (*).md, 3-install certificates.md, 9-compare k8s tools.md
                                   # Standalone setup/reference docs for the tutorial series
```

Excluded from the tree above (per instructions/format norms): `node_modules/`, `bin/`, `obj/`, `dist/`, `.git/`, `.run/` (compose log capture), IDE folders (`.vscode/`).

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
| Azure.Storage.Blobs | 12.24.0 (present but unused — see Finding F-1) | `src/common/mytravels.storage/mytravels.storage.csproj` |
| Magick.NET-Q16-AnyCPU | 14.10.0 | `src/messaging/mytravels.messaging/mytravels.messaging.csproj` |
| Flurl.Http | 4.0.2 | `src/common/mytravels.common/mytravels.common.csproj` |
| Polly | 8.5.2 | `src/common/mytravels.common/mytravels.common.csproj` |
| Swashbuckle.AspNetCore (Swagger) | 9.0.5 | `src/api/mytravels.api/mytravels.api.csproj` |
| Newtonsoft.Json | 13.0.4 | `src/api/mytravels.api/mytravels.api.csproj`, `mytravels.common.csproj` |
| MetadataExtractor (EXIF) | 2.8.1 | `src/common/mytravels.contract/mytravels.contract.csproj`, `mytravels.common.csproj` |
| Geolocation | 1.2.1 | `src/common/mytravels.domain/mytravels.domain.csproj` |
| System.Security.Cryptography.Xml | 10.0.10 (outlier vs. 9.x elsewhere) | `src/common/mytravels.domain/mytravels.domain.csproj:40` |
| React | 19.2.8 | `src/web/package-lock.json` |
| React DOM | 19.2.8 | `src/web/package-lock.json` |
| react-leaflet | 5.0.0 | `src/web/package-lock.json` |
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
| `mytravels.api` | Public REST surface | HTTP request/response, Swagger docs, CORS, global exception mapping | `PointOfInterestController`, `PlaceController`, `ApiExceptionMiddleware` | common, contract, domain, storage |
| `mytravels.messaging` | Async processing | Image resize, address resolution, retry sweep | `ResizeImage`, `AppendFormattedAddress`, `AppendFormattedAddressSweeper` (all `IHostedService`/`BackgroundService`) | common, contract, domain, storage |
| `mytravels.migration` | Schema management | Produces `efbundle` — a self-contained EF Core migration-apply executable; not itself a migration runner at execution time (see Finding F-3) | `CoreDbContextFactory` (design-time only) | domain |
| `mytravels.common` | Cross-cutting services | Geocoding (Google/OSM), RabbitMQ publish/subscribe base classes, cron scheduling base class | `GoogleMapsService`, `OpenStreetMapsService`, `ImageMetadataService`, `MessagePublisher`, `MessageSubscriberBase<T>`, `CronJobBase` | contract |
| `mytravels.contract` | Shared data shapes | Entities, DTOs, interfaces, exceptions, constants | `PointOfInterest`, `Tag`, `*Dto`, `ICoreDbContext`, `IObjectStorageService`, `IMapsService` | none (leaf) |
| `mytravels.domain` | Persistence | EF Core DbContext, migrations, stored-proc invocation, POI business logic | `CoreDbContext`, `PointOfInterestService` | contract |
| `mytravels.storage` | Object storage abstraction | Blob/object read-write | `MinIOStorageService` (active), `AzureStorageService` (dead code, unregistered) | contract |
| `web` | User interface | Map rendering, upload flow, place search | `App.tsx`, `MapView`, `UploadButton`, `PoiDialog`, `LocationSearchDialog` | none (calls API over HTTP only) |

---

## 6. Public Interfaces & APIs

### 6.1 `mytravels.api` — base path `/api`, no versioning, all endpoints anonymous (no `[Authorize]` anywhere)

| Method | Route | Request | Response | Status codes | Auth |
|---|---|---|---|---|---|
| GET | `/api/pointofinterest` | — | `List<PointOfInterestDto>` | 200 | none |
| GET | `/api/pointofinterest/filter?filterString=` | query `filterString` | `List<PointOfInterestDto>` | 200 | none |
| GET | `/api/pointofinterest/{id:int}?resizedImage=bool` | route `id`; query `resizedImage` is accepted but never used (Finding F-2) | `string` (base64 image) | 200 | none |
| PUT | `/api/pointofinterest` | multipart: `image` (file), query `pointOfInterestKey` | `SaveEntityResponseDto` | 200; 403 if image/key missing | none |
| POST | `/api/pointofinterest/image` | multipart: `image` (file) | `SaveEntityResponseDto` | 200; 403 if image missing | none |
| POST | `/api/pointofinterest/image/coordinates` | multipart: `image` (file) + `coordinates` (`SaveCoordinatesDto`: Latitude/Longitude `[Required][Range]`, FormattedAddress) | `SaveEntityResponseDto` | 200; 403 if image missing; 400 if `ModelState` invalid | none |
| GET | `/api/place?query=&limit=` | query `query` (required, non-blank), `limit` | `List<PlaceDto>` | 200; 403 if `query` blank | none |

Global error shape (`ApiErrorDto`, from `ApiExceptionMiddleware.cs`): `{ Id, HttpStatusCode, Message, Title, Links, Code (unset), Detail (unset) }`. Exception→status mapping: `RequiredParameterNotFoundException`→403, `OutOfRadiusException`→403, `DataNotFoundException`→404, `ApiException`→its own `.StatusCode`, anything else→500 with the raw exception message serialized into the response body (Finding F-8, information disclosure). Root path `/` is special-cased to return plain text `"API is running..."` (not a structured health check).

Swagger UI is mounted at `/swagger` in **every environment**, not gated to Development (Finding F-4).

### 6.2 `mytravels.messaging` — HTTP surface

Only a catch-all `MapGet("{**path}", ...)` returning `"Service is running..."` — exists purely so the container has a bindable HTTP port for orchestrator liveness checks; the real work happens via hosted services (§6.3), not HTTP.

### 6.3 Message-queue interfaces (RabbitMQ, both fanout exchanges, durable queue = exchange name, no DLX/TTL/max-length arguments)

| Exchange/Queue | Publisher | Consumer | Payload |
|---|---|---|---|
| `append-formatted-address` | `PointOfInterestService.CreatePointOfInterestAsync` | `AppendFormattedAddress` (HostedService) | `PointOfInterestMessage { CorrelationId, PointOfInterestId }` |
| `resize-image` | `PointOfInterestService.CreatePointOfInterestAsync`, `UpdatePointOfInterestAsync` | `ResizeImage` (HostedService) | `PointOfInterestMessage { CorrelationId, PointOfInterestId }` |

### 6.4 `ICoreDbContext` (library interface consumed within the .NET solution)

Exposes `DbSet`s plus domain methods: `GetPointsOfInterestAsync`, `GetPointsOfInterestByTagAsync`, `GetPointsOfInterestByKeyAsync`, `GetAllPointsOfInterestAsync`, `CreatePointOfInterestAsync`, `UpdatePointOfInterestTagsAsync`, `AddImageToPointOfInterestAsync`, `UpdateAddressAsync`, plus generic `Add`/`Delete`/`DetachObject`/`ExecuteSqlInterpolatedAsync`.

### 6.5 `IObjectStorageService` (library interface, two implementations — see §5, §17)

`GetBase64Async`, `GetObjectAsync<T>`, `GetStreamAsync`, `ListObjectsAsync`, `ListBucketsAsync`, `ObjectExistsAsync`, `RemoveObjectAsync`, `SaveObjectAsync` (×3 overloads), `SaveBase64StringAsync`.

### 6.6 Web frontend → API calls (`src/web/src/api/client.ts`)

| # | Method | Path | Purpose |
|---|---|---|---|
| 1 | GET | `/api/PointOfInterest` | List POIs (initial load + polling) |
| 2 | GET | `/api/PointOfInterest/{id}?resizedImage={bool}` | Fetch a POI's photo (base64 `text/plain`) |
| 3 | POST | `/api/PointOfInterest/image` | Upload photo with EXIF GPS |
| 4 | POST | `/api/PointOfInterest/image/coordinates` | Upload photo + manually-picked place |
| 5 | GET | `/api/Place?query=` | Place search (debounced 400ms, min 3 chars) |

---

## 7. Business Rules & Workflows

### 7.1 Upload with GPS EXIF (happy path)

1. User selects a photo in `UploadButton` → `POST /api/pointofinterest/image` (multipart).
2. `PointOfInterestService.SaveFileAsPointOfInsterestAsync` (typo preserved from source, `IPointOfInterestService.cs`) saves the raw bytes to MinIO bucket `uploaded-images` (`BucketNames.NewUploadedImagesContainer`).
3. `ImageMetadataService.GetLocationAsync` (via `MetadataExtractor`) reads EXIF GPS tags; if absent, falls back to `GeoLocation(0,0)` / `null` date-taken.
4. A `PointOfInterest` row is inserted (`Latitude`/`Longitude` possibly `(0,0)` if no EXIF — this is a valid, non-error state, distinguished later by the frontend's `hasCoordinates` check).
5. Two messages are published: `append-formatted-address` and `resize-image` (both fire even when coordinates are `(0,0)` — see Finding F-13, geocoding a null island).
6. `AppendFormattedAddress` consumer resolves and writes `FormattedAddress` (skipped if already non-empty — idempotent).
7. `ResizeImage` consumer resizes the image to 10% of original dimensions, uploads to `resized-images`, sets `ImageResized = true` (skipped if already `true` — idempotent).
8. Frontend polls `GET /api/PointOfInterest` every 3s (`POLL_INTERVAL_MS`), up to 10 attempts (`POLL_MAX_ATTEMPTS`), watching for the new POI's coordinates to become non-zero.

### 7.2 Upload without GPS EXIF

1. Same upload UI, but backend has no coordinates to extract.
2. **Inferred from `UploadButton.tsx`'s two-mode menu**: user is prompted ("No GPS on this photo? Search for the place instead") to open `LocationSearchDialog`, search via `GET /api/place?query=`, select a result.
3. `POST /api/pointofinterest/image/coordinates` is called instead, with the chosen `Latitude`/`Longitude`/`FormattedAddress` attached directly — `FormattedAddress` is supplied by the client in this path, so the async geocoding step is effectively redundant here (still published, per step 7.1.5) but a no-op since `FormattedAddress` is already non-empty.

### 7.3 Address-resolution retry sweep

Every 30 minutes, `AppendFormattedAddressSweeper` scans **all** `PointOfInterest` rows, retries geocoding for any row created within the last 2 days with an exactly-empty (`== ""`) `FormattedAddress`. Rows with `NULL` `FormattedAddress` (a valid DB state, column is nullable) are **not** picked up by this sweep even though the live consumer does treat null as needing resolution (`IsNullOrEmpty(...Trim())`) — see Finding F-11. Rows older than 2 days that never resolved are abandoned permanently by the sweep (still resolvable if the original message is somehow redelivered, but no active retry path exists for it after 2 days).

### 7.4 Tagging

`SaveEntityResponseDto`/`SavePointOfInterestDto` support attaching free-text tags to a POI via `UpdatePointOfInterestTagsAsync`, which serializes the tag list to JSON and calls stored procedure `spUpdatePointOfInterestTags`. Tag names are unique (`Tags.Name` has a unique index).

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

Navigation: `PointOfInterestTagAssociations` (1:N), `PointOfInterestAuditLogs` (1:N).

**Tag** (table `Tags`): `Id` PK, `Name varchar(30)` **unique index**, `DateCreated`.

**PointOfInterestTagAssociation** (join table): `Id` PK, `PointOfInterestId` FK (cascade, indexed, required), `TagId` FK (cascade, indexed, required), `DateCreated`.

**PointOfInterestAuditLog** (table `PointOfInterestAuditLogs`): `Id` PK, `QueueName varchar(100)`, `Payload varchar(500)`, `Sucessful bool` (misspelled, default `false`), `ErrorMessage varchar(500)`, `PointOfInterestId` FK (cascade, indexed, required), `DateCreated`.

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

`spGetPointOfInterest()` (no args — fetch all), `spGetPointOfInterestByTagName(tagName)`, `spGetPointOfInterestById(id)`, `spUpdatePointOfInterestTags(json)`. Invoked via `FromSqlRaw`/`FromSqlInterpolated`/`ExecuteSqlInterpolatedAsync` in `CoreDbContext.cs`.

---

## 9. State Management & Persistence

- **PostgreSQL 17.6** — sole system of record for POI metadata, tags, and audit logs. `CoreDbContext` is configured **globally `NoTracking`** (`QueryTrackingBehavior.NoTracking`), meaning any write path must manually attach/mark entities modified — `UpdateAddressAsync` does this explicitly (`EntityState.Unchanged` + per-property `IsModified = true`).
- **MinIO (S3-compatible)** — binary image storage, two buckets: `uploaded-images` (originals) and `resized-images` (thumbnails), both lazily auto-created on first write, not at startup.
- **RabbitMQ** — transient message bus for async work; no persistence guarantees beyond `durable: true` queue declaration (Observed in `MessageSubscriberBase.cs`) — no DLX, so message loss/poison-loop risk exists (§12, §17).
- **No caching layer** (no Redis/in-memory cache) anywhere in the stack.
- **Transaction boundaries**: each EF Core `SaveChangesAsync` call is its own implicit transaction; no explicit multi-statement `BeginTransaction`/`Commit` usage found in `CoreDbContext.cs`. `EnableRetryOnFailure()` is set on the Npgsql provider (transient-fault retry, not app-level transactions).
- **Ordering guarantees**: none — RabbitMQ fanout with a single unordered queue per exchange; concurrent processing of `resize-image` and `append-formatted-address` for the same POI can interleave arbitrarily (they touch different columns, so this is currently benign).

---

## 10. Configuration & Environment

### 10.1 `mytravels.api` (`appsettings.json` / `appsettings.Development.json`)

| Key | Default (appsettings.json) | Required? | Breaks if missing |
|---|---|---|---|
| `CorsHosts` | `*` | effectively yes | `NullReferenceException` at startup (`.Split(';')` on a null value) if the key is entirely absent from config |
| `ConnectionStrings:CoreDbContext` | `Host=localhost;...;Username=user123;Password=password123` | yes | `InvalidOperationException` at DI registration |
| `RabbitMQ:Uri` | `amqp://user123:password123@localhost:5672` | yes | connection factory throws on first use |
| `MinIO:Endpoint` / `AccessKey` / `SecretKey` | `localhost:9000` / `user123` / `password123` | yes for any storage call | `MinioClient` build fails / calls throw |
| `GoogleApiKey` | `<YOUR_GOOGLE_API_KEY>` (placeholder) | no — placeholder triggers automatic fallback to OpenStreetMap | geocoding silently switches provider, not an error |
| `GoogleMapsUrl` | `https://maps.googleapis.com` | only if Google provider active | |
| `GooglePlacesUrl` | `https://places.googleapis.com` | **dead config — never read** | n/a |
| `AllowedHosts` | `*` | no | ASP.NET Core default host-filtering |

### 10.2 `mytravels.messaging` — identical config surface to `mytravels.api` (same `appsettings.json` shape, same keys, same defaults).

### 10.3 `mytravels.migration` — `ConnectionStrings:CoreDbContext` only, plus whatever `--connection` argument is passed to the built `efbundle` at runtime (Kubernetes Job overrides via `$(ConnectionStrings__CoreDbContext)` env var, `4-argocd/manifests/migrations/job.yaml`).

### 10.4 `mytravels.storage`

`MinIOConfig`: `Endpoint`, `AccessKey`, `SecretKey` (bound from `MinIO` section). Azure path reads `StorageAccountConnectionString` directly via raw `IConfiguration.GetValue` — **this key exists in no `appsettings.json` in the repo**, consistent with `AzureStorageService` being fully dead code (§17, F-1).

### 10.5 `web` (Vite build-time env)

`VITE_API_BASE_URL` — baked into the static bundle at Docker build time via `ARG`/`ENV` (not runtime-configurable). Defaults to `http://localhost:5101` if unset (`src/web/src/api/client.ts:3`).

### 10.6 Docker Compose / Kubernetes env-var surface (cross-stage, from deployment-topology research)

| Variable | Present in Compose stages? | Present in k8s manifests? |
|---|---|---|
| `GOOGLE_API_KEY` | yes (1, 2) | **no — dropped entirely, Finding F-14** |
| `GRAPH_API_TOKEN` | yes (`.env.example`, stages 0–3) | no — and never consumed by any Compose service either; dead variable |
| `VITE_API_BASE_URL` | yes, value differs per stage (`http://localhost:5101` in 1/2, `http://api.mytravels.local:8080` in 3) | n/a (baked at web image build time, stage 4 doesn't build images) |
| `MINIO_ENDPOINT` / console port | consistent `9090:9090` everywhere | consistent, `minio-console` Service port `9090` |
| Argo CD-only vars (`ARGOCD_USER`, `ARGOCD_PASSWORD`, `CONTENT_SAFETY_ENDPOINT`, `CONTENT_SAFETY_KEY`) | only in `4-argocd/.env.example` | applied out-of-band via `kubectl create secret`, not committed |

Stage 4's `.env.example` is structurally distinct from stages 0–3 (drops most app config vars since it never builds images or runs Compose — `.env` there is purely input to a "create the k8s secrets" runbook step).

---

## 11. Authentication, Authorization & Security

**Observed:** there is **no authentication or authorization anywhere in the application layer.**
- No `AddAuthentication`/`AddAuthorization`/`UseAuthentication`/`UseAuthorization` in either `mytravels.api/Program.cs` or `mytravels.messaging/Program.cs`.
- No `[Authorize]` attribute exists anywhere in the solution.
- Every REST endpoint, including image upload, is fully anonymous.
- Swagger UI is exposed unauthenticated in every environment (not gated behind `IsDevelopment()`).
- CORS policy `AllowSpecificOrigin` uses `AllowAnyHeader()`+`AllowAnyMethod()`, restricted only by the `CorsHosts` origin list (default `*` in `appsettings.json`, meaning **any origin** unless overridden).

**Secrets handling:**
- Local dev credentials (`user123`/`password123` for DB/RabbitMQ/MinIO) are committed in plaintext in `appsettings.json` across `mytravels.api`, `mytravels.messaging`, and `mytravels.migration`.
- `appsettings.Development.json` (gitignored, `.gitignore:59`) contains a real-format Google Maps API key on this machine — not committed to the repo, consistent with the April 2026 secret-scrub incident that added this gitignore pattern.
- `3-kubernetes/manifests/messaging/1-secret.yaml` (also gitignored via `*secret.yaml`, `.gitignore:132`) contains, on this machine, a real-looking base64-encoded Azure Content Safety endpoint/key — again local-only, not committed.
- Stage 4 (`4-argocd`) removes **all** Secret manifests from the repo entirely, applying them out-of-band via `kubectl create secret` from `.env` — a deliberate, documented fix over stage 3's committed-secret pattern (which itself is gitignored per-file, not structurally prevented).
- Argo CD is configured with `server.insecure: true` (TLS disabled) and a local admin-equivalent account (`user123`, `role:admin`), intended for the local k3d tutorial context, not production.
- No PII handling policy is evident; uploaded photos may contain identifying metadata (EXIF), which is read (for GPS) but not stripped before storage — thumbnails and originals both retain any other embedded EXIF fields.
- No rate limiting, no CSRF protection (not applicable to a token-less anonymous JSON API, but also means no anti-automation protection on the upload endpoint).

---

## 12. Error Handling & Retries

**API (`mytravels.api`):** centralized in `ApiExceptionMiddleware`. Custom exception → HTTP status: `RequiredParameterNotFoundException`→403 (semantically should be 400), `OutOfRadiusException`→403, `DataNotFoundException`→404, `ApiException`→its own status, everything else→500 with the raw `Exception.Message` serialized to the client (information disclosure risk). All client and server errors are logged at `LogError` level (no severity differentiation).

**EF Core:** `EnableRetryOnFailure()` is set on the Npgsql provider for all three DbContext-owning projects (api, messaging, migration) — transient network/connection retry, count/backoff not overridden from Npgsql defaults (Observed: no explicit retry-count argument passed).

**Geocoding (Polly, inline in `GoogleMapsService`/`OpenStreetMapsService`):** `WaitAndRetryAsync(2, i => TimeSpan.FromSeconds(Math.Pow(2, i)))` — 2 retries, exponential backoff (~2s, ~4s), no jitter, no circuit breaker, no overall timeout policy.

**RabbitMQ consumers (`MessageSubscriberBase`):** manual ack/nack; any exception in `ProcessMessageAsync` results in `nack(requeue: true)` with **no dead-letter exchange, no TTL, no max-length** configured on the queue — a permanently-failing message (e.g., referencing a deleted `PointOfInterestId`) will requeue and be immediately redelivered forever (infinite redelivery loop, no backoff). `ResizeImage`'s consumer has no dedicated `catch` block (only a `finally` releasing its semaphore), so its failures are less diagnosable than `AppendFormattedAddress`'s (which does log inside a `catch`).

**Sweeper (`CronJobBase`):** per-tick try/catch logs and swallows exceptions so one bad tick doesn't kill the 30-minute loop — but this also means sweep failures are easy to miss without active log monitoring.

**Idempotency:** both message consumers explicitly short-circuit if their target field is already set (`FormattedAddress` non-empty / `ImageResized == true`), making at-least-once delivery safe for the steady-state case; this doesn't protect against the concurrent-write race described in Finding F-12 or against non-transient failures looping forever (above).

**Storage layer:** `MinIOStorageService.ListObjectsAsync` and `RemoveObjectAsync` both catch generic `Exception`, `Console.WriteLine` it, and return normally — callers get no signal that a list is incomplete or a delete silently failed.

**Frontend:** `App.tsx`'s initial load and polling fetches both swallow errors with empty catch handlers — no user-visible error/retry UI if the API is unreachable (the map simply stays empty). `UploadButton.tsx`'s catch block discards the underlying `Error` (which does carry the real HTTP status/body from `throwForStatus`) and shows one static failure message regardless of cause.

---

## 13. Concurrency, Async & Scheduling

**Hosted services (`mytravels.messaging`, all registered in `Program.cs`, run concurrently in one process):**
1. `AppendFormattedAddress` — RabbitMQ consumer, `prefetchCount: 10`, but internally serializes all processing behind a `static SemaphoreSlim(1,1)` — the prefetch headroom is never actually used; messages are handled one at a time.
2. `AppendFormattedAddressSweeper` — `CronJobBase`-derived, runs immediately on startup then every 30 minutes via `PeriodicTimer`, for the life of the process.
3. `ResizeImage` — same consumer pattern/prefetch/semaphore serialization as #1.

**Race condition (Finding F-12):** `AppendFormattedAddress` guards itself with a semaphore local to that class; `AppendFormattedAddressSweeper` has **no locking at all** and runs in the same process concurrently — a POI could be read as unresolved by both simultaneously, geocoded twice, and written twice. No optimistic-concurrency token exists on `PointOfInterest` to catch this at the DB layer.

**Connection lifecycle:** `MessagePublisher.PublishAsync` opens a brand-new AMQP connection + channel **per publish call**, declares the exchange, publishes, and closes both — no pooling/reuse, a latency/throughput concern under any real load. Separately, `Program.cs` also registers a DI-singleton `IConnectionFactory` that only `MessagePublisher` consumes, while `MessageSubscriberBase` builds its own `ConnectionFactory` independently from raw config — duplicated construction logic, not shared.

**No locking/queueing at the database layer** beyond what Postgres's MVCC provides implicitly; no `SELECT ... FOR UPDATE` or EF Core concurrency tokens anywhere in `CoreDbContext.cs`.

---

## 14. External Integrations & Third-Party Dependencies

| Integration | Endpoint | Auth | Failure mode |
|---|---|---|---|
| Google Maps Geocoding API | `GoogleMapsUrl` config (`https://maps.googleapis.com`) | API key (`GoogleApiKey` query/header) | Polly: 2 retries, exp. backoff; `InvalidOperationException` thrown on non-OK or missing `formatted_address` after retries exhausted |
| OpenStreetMap Nominatim (fallback geocoder, used when `GoogleApiKey` is unset/placeholder) | `OpenStreetMapsUrl` config, default `https://nominatim.openstreetmap.org` | none (public API), `User-Agent: mytravels/1.0` default | same Polly retry pattern |
| OpenStreetMap tile server (frontend only) | `https://tile.openstreetmap.org/{z}/{x}/{y}.png` | none | no fallback/rate-limit handling if OSM throttles |
| MinIO (S3-compatible) | `MinIO:Endpoint` config | static access/secret key | swallowed exceptions on list/remove (see §12) |
| Azure Blob Storage | never invoked — `AzureStorageService` is unregistered dead code | connection-string (never configured) | n/a — unreachable code path |
| Azure Content Safety (messaging-secret, k8s only) | `ContentSafetyEndpoint`/`ContentSafetyKey` secret keys exist in `3-kubernetes/manifests/messaging/1-secret.yaml` | API key | **no code in `src/messaging` references `ContentSafety*` anywhere** — the Secret provisions credentials for an integration that doesn't exist in the current codebase (dead config, or a feature removed without cleaning up its secret — see Finding F-15) |
| RabbitMQ | `RabbitMQ:Uri` | username/password in URI | no DLX, infinite redelivery on poison messages (§12) |

---

## 15. Runtime Behaviour & Edge Cases

- **Startup (api/messaging):** DI container built, EF Core context registered (throws immediately if connection string missing), RabbitMQ `IConnectionFactory` built (lazy — doesn't connect until first use), culture forced to `InvariantCulture` process-wide. No explicit readiness gate on RabbitMQ/Postgres availability at startup beyond `EnableRetryOnFailure()`; in Kubernetes, ordering is enforced externally via Job/init-container dependencies (migration Job runs before api/messaging Deployments are expected to work, though nothing blocks them from starting concurrently).
- **Health/liveness:** api's `livenessProbe` hits `/swagger` (not a dedicated health endpoint); messaging's `livenessProbe` is commented out in both k8s manifest sets; neither has a `readinessProbe`. Only `web`'s Deployment has both probes, checking `/`.
- **Empty input:** uploading with no `image` file → `RequiredParameterNotFoundException` → HTTP 403 (not 400).
- **Malformed/no-EXIF image:** falls back to `(0,0)` coordinates and `null` date-taken — not treated as an error; the frontend's `hasCoordinates` filter is the only place this is distinguished from a "real" pin.
- **Oversized input:** no explicit request body size limit configured anywhere found in `Program.cs` (relies on ASP.NET Core/Kestrel defaults).
- **Network failure to geocoding provider:** 2 Polly retries then propagates as an unhandled `InvalidOperationException`, which — since it's thrown inside a RabbitMQ consumer, not an HTTP request — results in `nack(requeue: true)` and infinite redelivery (§12), not a clean failure state.
- **Partial failure (image resize succeeds, address resolution fails, or vice versa):** the two are fully independent messages/consumers: one can succeed while the other fails/retries indefinitely, leaving a POI with a thumbnail but no address (or vice versa) indefinitely if the failure is non-transient.
- **Shutdown:** no explicit graceful-shutdown/drain logic found in either hosted-service class; relies on ASP.NET Core's default `IHostedService.StopAsync` behavior (cancellation token propagation into the consumer loop's blocking wait).

---

## 16. Assumptions, Implicit Behaviour & Undocumented Conventions

- **Provider auto-selection is silent:** whichever of Google Maps or OpenStreetMap gets registered as `IMapsService` is decided entirely by whether `GoogleApiKey` equals the literal placeholder string `"<YOUR_GOOGLE_API_KEY>"` — there is no log line or startup banner announcing which provider is active.
- **`(0, 0)` is overloaded** as both "valid equatorial/prime-meridian coordinate" and "not yet geocoded" sentinel — the frontend's `hasCoordinates` check (`latitude === 0 && longitude === 0` → treat as unresolved) means a POI whose *true* location is genuinely `(0,0)` would be permanently hidden from the map.
- **The migration image's actual behavior is not visible from `Program.cs`** — a reader following only that file would conclude migrations are never applied; the real mechanism is a build-time `dotnet ef migrations bundle` producing a separate `efbundle` native executable that becomes the container's entrypoint (§5, §17 F-3).
- **Stored-procedure SQL is deployed via EF Core migrations** (`Features/**/*.sql` executed inside migration `Up()` methods), not tracked/versioned as ordinary application code — changing a stored procedure requires a new migration, not just editing the `.sql` file.
- **The web frontend's API base URL is fixed per Docker image build**, not per running container — the same built image cannot be pointed at a different API origin without rebuilding, which has direct consequences for how the k8s/Argo CD stages must be operated (a new `web` image per environment/target URL, version-pinned per stage — see the v1.0.4 vs v1.0.5 skew in Finding F-14).
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

### Fragile or risky logic

- **F-4** — Swagger UI is mounted unconditionally in every environment (not gated by `IsDevelopment()`), and combined with zero authentication anywhere, the full API surface/schema is discoverable in any deployment of this image as-is. `src/api/mytravels.api/Program.cs:86-91`.
- **F-5** — No authentication/authorization anywhere in the API — every endpoint including image upload is fully anonymous; `CorsHosts` defaults to `*`. `src/api/mytravels.api/Program.cs` (no `UseAuthentication`/`UseAuthorization` calls); `appsettings.json:CorsHosts`.
- **F-7** — `UseHttpsRedirection()` is called unconditionally, but the container only exposes/binds HTTP (`ASPNETCORE_URLS=http://0.0.0.0:5101`) — the redirect middleware has no HTTPS port to target inside the container, effectively dead unless TLS termination happens upstream and the app is never actually asked to redirect. `src/api/mytravels.api/Program.cs:105`, `Dockerfile:27-29`.
- **F-9** — Every first-party .NET project targets `net10.0` but pins EF Core / Npgsql / Microsoft.Extensions.* packages to the `9.0.x` line — a consistent framework/package version skew across the whole solution. All `*.csproj` files under `src/`.
- **RabbitMQ consumers have no dead-letter exchange, TTL, or max-length** on their queues — any permanently-failing message (e.g., a stale `PointOfInterestId`) triggers infinite `nack(requeue:true)` redelivery with no backoff. `src/common/mytravels.common/Services/MessageSubscriberBase.cs` (queue declaration, no `arguments`).
- **`MessagePublisher` opens a new AMQP connection+channel per publish call** — no pooling, a throughput/latency risk under load. `src/common/mytravels.common/Services/MessagePublisher.cs:19-37`.
- **`AppendFormattedAddressSweeper` does a full unfiltered `SELECT *` on `PointOfInterests` every 30 minutes**, filtering in memory — scales poorly as the table grows. `src/common/mytravels.domain/CoreDbContext.cs:32-33` (`GetPointsOfInterestAsync`) via `AppendFormattedAddressSweeper.cs:31`.
- **`CoreDbContext.UpdateAddressAsync` loads the entire `PointOfInterests` table into memory** and filters by key in C# rather than querying by key directly, despite the context being globally `NoTracking` (requiring manual `EntityState`/`IsModified` toggling to make the update stick). `src/common/mytravels.domain/CoreDbContext.cs:85-102`.
- **MinIO image is pulled unpinned (`quay.io/minio/minio`, no tag)** in every Compose file — reproducibility risk. `0-local/docker-compose.yml`, `1-dockerize/docker-compose.yml`, etc. (minio service `image:` line).
- **Migration/`db-migrations` Job has no resource requests/limits** set, unlike every other Deployment in the same manifest set. `3-kubernetes/manifests/migrations/2-job.yaml`, `4-argocd/manifests/migrations/job.yaml`.
- **No `readinessProbe` on postgres/rabbitmq/minio/api Deployments** (only `livenessProbe`); messaging's `livenessProbe` is commented out entirely. `3-kubernetes/manifests/{postgres,rabbitmq,minio,api,messaging}/*deployment.yaml`.
- **Orphan `rabbitmq-config-pvc`** created but never mounted by the RabbitMQ Deployment in stage 3 (fixed in stage 4 per commit `4ced81d`, but the stage-3 manifest was never updated to match). `3-kubernetes/manifests/rabbitmq/3-pvc.yaml` vs. `4-deployment.yaml`.
- **`3-kubernetes/manifests/messaging/1-secret.yaml` provisions `ContentSafetyEndpoint`/`ContentSafetyKey`** for an Azure Content Safety integration that no code in `src/messaging` references at all — a secret kept for a feature that isn't (or is no longer) implemented (F-15).
- **`mytravels.migration.csproj` targets `net10.0` but its EF Core/Hosting/Npgsql packages and pinned `dotnet-ef` CLI tool are all on the `9.0.x` line** — consistent with F-9 but worth flagging separately since it directly affects the migration-bundle build. `src/common/mytravels.migration/mytravels.migration.csproj`, `src/.config/dotnet-tools.json`.

### Dead code / ambiguous logic / undocumented behaviour

- **F-1** — `AzureStorageService` is fully implemented but never registered in any DI container, and its required config key (`StorageAccountConnectionString`) exists in no `appsettings.json` in the repo — dead code with a broken namespace (`mytravels.common.Services` inside the `mytravels.storage` project, unlike the correctly-namespaced `MinIOStorageService`). `src/common/mytravels.storage/AzureStorageService.cs:12,25`.
- **F-3** — `mytravels.migration`'s `Program.cs` registers a DbContext and calls `host.RunAsync()` with no hosted services and no `Database.Migrate()` call anywhere — it never applies migrations itself. The real runtime artifact is a separately built `efbundle` executable (`dotnet ef migrations bundle`), set as the container `ENTRYPOINT`. A reader following only `Program.cs` would be misled about how migrations actually run. `src/common/mytravels.migration/Program.cs:8-23`, `Dockerfile:21-26,48`.
- **F-10** — `Hosted/` directory in `mytravels.api` exists but is completely empty; `Services/` and `Profiles/` directories in `mytravels.messaging` exist but are empty and not even git-tracked; `mytravels.common/Config/`, `Models/`, `Policies/` and `mytravels.contract/Attributes/` are likewise empty placeholders with no tracked files — the shared-library layout implies more structure than currently exists (Polly policies, for instance, are defined inline in the maps services rather than in the empty `Policies/` folder).
- **F-13** — `resize-image` and `append-formatted-address` messages are published unconditionally on POI creation, even when EXIF extraction yielded `(0,0)`/no coordinates — geocoding a `(0,0)` "null island" coordinate is attempted and will resolve to *something* (likely a nonsensical mid-ocean address) rather than being skipped. `src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs:108-109`.
- **`SeedData` migration (`20600930075821_SeedData.cs`) and `UpdateStoredProcedure` migration (`20590930075747_...`) carry migration timestamps decades in the future** (2059, 2060) relative to `Init`'s 2026 timestamp — almost certainly placeholder/bogus values rather than real generation dates; both also have non-reversible `Down()` methods (`NotSupportedException`/`NotImplementedException`). `src/common/mytravels.domain/Migrations/20590930075747_UpdateStoredProcedure.cs`, `20600930075821_SeedData.cs`.
- **`SeedData` migration references a `SeedScripts/` folder that does not exist anywhere in source** (only stale copies remain in `bin/Debug` build output) — the migration silently no-ops today. `src/common/mytravels.domain/Migrations/20600930075821_SeedData.cs:14-15`.
- **Misspelled/typo identifiers preserved through the codebase**: `PointOfInterestAuditLog.Sucessful` (should be "Successful"), `IPointOfInterestService.SaveFileAsPointOfInsterestAsync` (should be "Interest") — both load-bearing (renaming requires touching DB column names and calling code across the solution). `src/common/mytravels.contract/Entities/PointOfInterestAuditLog.cs:16`; `src/common/mytravels.contract/Interfaces/IPointOfInterestService.cs:11-12`.
- **Commented-out dead attribute** `//[Index(IsUnique = true)]` on `Tag.Name` — the actual uniqueness constraint is enforced separately via fluent API in `CoreDbContext.OnModelCreating`, making the comment misleading if read in isolation. `src/common/mytravels.contract/Entities/Tag.cs:13`.
- **`ApiException`'s serialization constructor unconditionally throws `NotImplementedException`** — a dead/broken deserialization path (only matters if this exception type is ever cross-AppDomain/remoted, unlikely in this architecture but technically broken). `src/common/mytravels.contract/CustomException/ApiException.cs:23-26`.
- **Copy-pasted MinIO SDK example code left in the production path**: `Console.WriteLine("Running example for API: ...")` calls and an upstream example-repo comment inside `ListObjectsAsync`/`RemoveObjectAsync`, both of which also swallow exceptions via generic `catch (Exception e) { Console.WriteLine(...); }` instead of using the app's `ILogger`. `src/common/mytravels.storage/MinIOStorageService.cs:240,256,268-271,307,310-313`.
- **`GooglePlacesUrl` config key is declared but never read** anywhere in `GoogleMapsService.cs`. `src/api/mytravels.api/appsettings.json:12`, `src/messaging/mytravels.messaging/appsettings.json:12`.
- **`GRAPH_API_TOKEN` env var is declared in `.env.example` for stages 0–3 but never consumed** by any Compose service or k8s manifest — dead variable, dropped in stage 4.
- **Misleading `launchSettings.json` profile name**: the messaging project's only launch profile is named `mytravels.api`, evidently copy-pasted from the API project. `src/messaging/mytravels.messaging/Properties/launchSettings.json:3`.
- **`mytravels.api.http` sample file references `GET /jobs/`**, a route that exists in neither controller — stale sample request. `src/api/mytravels.api/mytravels.api.http:3`.
- **`mytravels.common.csproj` excludes a self-named nested `mytravels.common\**` path that doesn't exist** — vestigial template leftover. `src/common/mytravels.common/mytravels.common.csproj:10-12`.
- **Stored procedure filename/function-name mismatch**: file `spGetPointOfInterests.sql` (plural) defines function `public.spGetPointOfInterest()` (singular) — confusing but not a runtime bug since callers use the correct singular name. `src/common/mytravels.domain/Features/PointOfInterest/spGetPointOfInterests.sql:1`.
- **`ingress.yaml` header comments (both stage 3 and stage 4) omit `messaging-ingress`** from their descriptive comment even though the manifest body defines it — documentation/body mismatch. `3-kubernetes/manifests/9-ingress.yaml:1-12,67-86`; `4-argocd/manifests/ingress.yaml:1-12,69-89`.
- **`scripts/init-dbs.sql` is a no-op** — `CoreDb` is already created via `POSTGRES_DB`, so the script exists only as an unused hook.
- **Web frontend's `README.md` is the unmodified Vite template README** — not project-specific documentation. `src/web/README.md`.
- **`tsconfig.app.json`/`tsconfig.node.json` do not visibly set `strict: true`**, unusual for a current Vite/React template default — worth confirming there's no extended base config, since none was found in the directory listing.
- **PV/nodeAffinity for MinIO is hard-pinned to a specific k3d node name** (`k3d-mytravels-agent-2`) in both k8s manifest stages — reasonable for a `hostPath`-backed tutorial cluster, but a portability constraint baked into application manifests rather than infra config.
- **Version skew between Compose and k8s `web` images**: `1-dockerize`/`2-dockerhub` Compose files run `mytravels-web:v1.0.4` while the build helper (`docker-compose.build.yml`) and both k8s manifest sets deploy `v1.0.5`.

---

## 18. Glossary

| Term | Meaning |
|---|---|
| **POI** | Point of Interest — the core domain entity: a geotagged photo with optional address/tags |
| **Null island** | Geodetic slang (used here as an inferred description, not a code identifier) for coordinate `(0,0)`, overloaded in this codebase as the "unresolved location" sentinel |
| **Fanout exchange** | RabbitMQ exchange type that broadcasts to every bound queue, ignoring routing key — used for both this app's exchanges |
| **Sweeper** | The `AppendFormattedAddressSweeper` cron-style background job that retries failed address resolutions on a timer |
| **efbundle** | A self-contained native executable produced by `dotnet ef migrations bundle`, used as the migration container's actual entrypoint (distinct from `mytravels.migration`'s `Program.cs`) |
| **Sync wave** | Argo CD annotation (`argocd.argoproj.io/sync-wave`) controlling the order in which manifests are applied during a GitOps sync |
| **Self-heal** | Argo CD feature (`syncPolicy.automated.selfHeal`) that automatically reverts manual cluster drift back to the Git-defined state; deliberately introduced as a separate lesson step in stage 4 rather than enabled from the start |
| **k3d** | A tool for running lightweight k3s Kubernetes clusters inside Docker, used as the tutorial's local cluster |
| **CKAD / CKA / CKS / KCNA** | CNCF/Linux Foundation Kubernetes certifications referenced in `CERTIFICATION.md` as the tutorial's target learning outcomes |

---

*End of specification. 18/18 sections present, none marked N/A. Findings: 15 lettered (F-1…F-15) plus additional unlettered items in the "fragile" and "dead code" sub-buckets — 30+ total findings.*
