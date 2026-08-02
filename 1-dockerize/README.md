# MyTravels

A geolocation-based Points of Interest (POI) management system built on .NET 10. Upload images or drop coordinates to track and tag locations, with asynchronous image resizing and automatic address resolution via Google Maps.

## Overview

MyTravels is a microservices-style application with two runnable services and a shared library layer:

- **API** — REST API for creating, querying, and managing POIs
- **Messaging** — Background worker that processes images and resolves addresses asynchronously
- **Migration** — Once-Off console app to apply EF Core database migrations

GPS coordinates are extracted from image EXIF data on upload. Addresses are fetched from Google Maps and written back asynchronously. Images are resized to thumbnails via a RabbitMQ queue.

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10.0 (C#) |
| Web Framework | ASP.NET Core |
| Database | PostgreSQL |
| ORM | Entity Framework Core 9 |
| Message Broker | RabbitMQ |
| Object Storage | MinIO (S3-compatible) or Azure Blob Storage |
| Image Processing | ImageMagick (Magick.NET) |
| API Docs | Swagger / OpenAPI |
| HTTP Client | Flurl.Http |
| Resilience | Polly |
| Containerisation | Docker (Alpine Linux) |

## Project Structure

```
2-dockerize/
├── api/
│   └── mytravels.api/              # REST API (port 5101)
├── messaging/
│   └── mytravels.messaging/        # Background worker (port 5102)
└── common/
    ├── mytravels.contract/         # Entities, DTOs, interfaces, constants
    ├── mytravels.common/           # Shared services (messaging, geo, cron)
    ├── mytravels.domain/           # EF Core DbContext, stored procedures, migrations
    ├── mytravels.storage/          # MinIO and Azure Blob Storage adapters
    └── mytravels.migration/        # Migration runner (console app)
```

**Dependency graph:**

```
api ─────────────────────────┐
messaging ───────────────────┤──► common ──► contract
                             ├──► domain ──► contract
                             └──► storage ─► contract
migration ───────────────────────► domain
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker
- Google Maps API key (for address resolution)

## Getting Started

### With Docker Compose (recommended)

All services — PostgreSQL, RabbitMQ, MinIO, migrations, API, and messaging worker — are orchestrated by Docker Compose. The `.env` file at the root of `2-dockerize/` holds all required values; edit it before running.

```bash
cd 2-dockerize
docker compose up --build
```

Swagger UI is available at `http://localhost:5101/swagger` once the stack is healthy.

### Running locally (without Docker Compose)

#### 1. Database

Start PostgreSQL and run migrations:

```bash
cd 2-dockerize/common/mytravels.migration
dotnet run
```

#### 2. Object Storage

Start MinIO locally:

```bash
docker run -p 9000:9000 -p 9001:9001 \
  -e MINIO_ROOT_USER=minioadmin \
  -e MINIO_ROOT_PASSWORD=minioadmin \
  minio/minio server /data --console-address ":9001"
```

The application expects two buckets: `uploaded-images` and `resized-images`. These are created automatically on startup.

#### 3. RabbitMQ

```bash
docker run -p 5672:5672 -p 15672:15672 rabbitmq:management
```

#### 4. API

```bash
cd 2-dockerize/api/mytravels.api
dotnet run
```

Swagger UI is available at `http://localhost:5101/swagger`.

#### 5. Messaging Worker

```bash
cd 2-dockerize/messaging/mytravels.messaging
dotnet run
```

## Configuration

### Docker Compose — `.env`

All Docker Compose services read environment variables from the `.env` file in the `2-dockerize/` directory.

| Variable | Description |
|---|---|
| `CORE_DB_CONTEXT` | PostgreSQL connection string used by the API, messaging worker, and migration |
| `RABBIT_MQ_URI` | RabbitMQ AMQP connection URI |
| `GOOGLE_API_KEY` | Google Maps Geocoding API key |
| `GOOGLE_MAPS_URL` | Google Maps base URL |
| `GOOGLE_PLACES_URL` | Google Places base URL |
| `MINIO_ROOT_USER` | MinIO access key (also used as `MinIO__AccessKey` inside containers) |
| `MINIO_ROOT_PASSWORD` | MinIO secret key (also used as `MinIO__SecretKey` inside containers) |
| `MINIO_ENDPOINT` | MinIO endpoint reachable from inside the Compose network (e.g. `minio:9000`) |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core environment (`Development`, `Production`) |
| `ASPNETCORE_URLS` | Listen URL for the service (e.g. `http://+:5101`) |

### Local development — `appsettings.Development.json`

When running with `dotnet run`, configure `2-dockerize/api/mytravels.api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "CoreDbContext": "Host=localhost;Port=5432;Database=CoreDb;Username=user123;Password=password123"
  },
  "RabbitMQ": {
    "Uri": "amqp://guest:guest@localhost:5672/"
  },
  "GoogleApiKey": "<YOUR_GOOGLE_API_KEY>",
  "GoogleMapsUrl": "https://maps.googleapis.com",
  "MinIO": {
    "Endpoint": "http://localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin"
  },
  "CorsHosts": "http://localhost:3000"
}
```

## API Reference

Base path: `/api/pointofinterest`

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/pointofinterest` | List all POIs |
| `GET` | `/api/pointofinterest/filter?filterString=` | Filter POIs by tag or address |
| `GET` | `/api/pointofinterest/pointOfInterestKey/{key}` | Get a specific POI by key |
| `GET` | `/api/pointofinterest/{id}?resizedImage=bool` | Get a POI image as base64 |
| `POST` | `/api/pointofinterest` | Save/update tags on one or more POIs |
| `POST` | `/api/pointofinterest/coordinates` | Create a POI from coordinates |
| `POST` | `/api/pointofinterest/image` | Upload an image (EXIF GPS data extracted automatically) |
| `PUT` | `/api/pointofinterest/status` | Update a POI's status |
| `PUT` | `/api/pointofinterest/address` | Update a POI's coordinates and address |
| `PUT` | `/api/pointofinterest/image` | Replace a POI's image |

Full interactive docs: `http://localhost:5101/swagger`

## Data Model

```
PointOfInterest
├── PointOfInterestKey       unique identifier (string)
├── Container                storage bucket name
├── OriginalFileName
├── GeneratedBlobName
├── Latitude / Longitude
├── FormattedAddress         resolved by Google Maps asynchronously
├── ImageResized             set to true after thumbnail is created
├── PointOfInterestType      (lookup)
├── PointOfInterestStatus    (lookup)
└── Tags                     many-to-many via PointOfInterestTagAssociation

Tag
└── Name                     unique

PointOfInterestAuditLog      history of status/address changes
```

**PostgreSQL schemas:**
- `public` — main tables
- `lookups` — PointOfInterestTypes, PointOfInterestStatuses
- `config` — EF Core migrations history

## Async Processing (Messaging Service)

Two RabbitMQ exchanges drive background work:

| Exchange | Trigger | Action |
|---|---|---|
| `resize-image` | Image uploaded | Resize to 10% of original dimensions, save to `resized-images` bucket |
| `append-formatted-address` | Coordinates saved | Call Google Maps Geocoding API, write formatted address back to the database |

The `AppendFormattedAddressSweeper` retries any records that failed address resolution.

## Docker

Three Compose files are provided:

| File | Purpose | Command |
|---|---|---|
| `docker-compose.yml` | Build images from source and run all services | `docker compose up --build` |
| `docker-compose.build.yml` | Build and push images to a container registry | `docker compose -f docker-compose.build.yml build --push` |
| `docker-compose.run.yml` | Run all services using pre-built registry images | `docker compose -f docker-compose.run.yml up` |

To build individual images without Compose:

```bash
docker build -f 2-dockerize/api/mytravels.api/Dockerfile            -t mytravels-api       .
docker build -f 2-dockerize/messaging/mytravels.messaging/Dockerfile -t mytravels-messaging .
docker build -f 2-dockerize/common/mytravels.migration/Dockerfile    -t mytravels-migration .
```

## Ports and Protocols

| Service | Protocol | Local Port | Docker Compose Port | Notes |
|---|---|---|---|---|
| API | HTTP | 5101 | 5101 | Swagger UI at `http://localhost:5101/swagger` |
| Messaging worker | HTTP | 5102 | 5102 | Internal background worker; no public UI |
| PostgreSQL | TCP | 5432 | 5432 | Default PostgreSQL port |
| RabbitMQ | AMQP | 5672 | 5672 | Message broker |
| RabbitMQ Management | HTTP | 15672 | 15672 | Management UI at `http://localhost:15672` |
| MinIO S3 API | HTTP (S3) | 9000 | 9000 | S3-compatible object storage |
| MinIO Console | HTTP | 9001 | 9090 | Web UI: `http://localhost:9001` (local) · `http://localhost:9090` (Compose) |
