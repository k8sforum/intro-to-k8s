# MyTravels

A geolocation-based Points of Interest (POI) management system built on .NET 10. Upload images or drop coordinates to track and tag locations, with asynchronous image resizing and automatic address resolution via Google Maps.

## Overview

MyTravels is a microservices-style application with two runnable services and a shared library layer:

- **API** - REST API for creating, querying, and managing POIs
- **Messaging** - Background worker that processes images and resolves addresses asynchronously
- **Migration** - Once-Off console app to apply EF Core database migrations

GPS coordinates are extracted from image EXIF data on upload. Addresses are fetched from Google Maps and written back asynchronously. Images are resized to thumbnails via a RabbitMQ queue.

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10.0 (C#) |
| Web Framework | ASP.NET Core |
| Database | PostgreSQL |
| ORM | Entity Framework Core 9 |
| Message Broker | RabbitMQ |
| Object Storage | SeaweedFS (S3-compatible) or Azure Blob Storage |
| Image Processing | ImageMagick (Magick.NET) |
| API Docs | Swagger / OpenAPI |
| HTTP Client | Flurl.Http |
| Resilience | Polly |
| Containerisation | Docker (Alpine Linux) |

## Project Structure

```
3-kubernetes/
├── api/
│   └── mytravels.api/              # REST API (port 5101)
├── messaging/
│   └── mytravels.messaging/        # Background worker (port 5102)
└── common/
    ├── mytravels.contract/         # Entities, DTOs, interfaces, constants
    ├── mytravels.common/           # Shared services (messaging, geo, cron)
    ├── mytravels.domain/           # EF Core DbContext, stored procedures, migrations
    ├── mytravels.storage/          # SeaweedFS (S3) and Azure Blob Storage adapters
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

All services - PostgreSQL, RabbitMQ, SeaweedFS, migrations, API, and messaging worker - are orchestrated by Docker Compose. The `.env` file at the root of `3-kubernetes/` holds all required values; edit it before running.

```bash
cd 3-kubernetes
docker compose up --build
```

Swagger UI is available at `http://localhost:5101/swagger` once the stack is healthy.

### Running locally (without Docker Compose)

#### 1. Database

Start PostgreSQL and run migrations:

```bash
cd 3-kubernetes/common/mytravels.migration
dotnet run
```

#### 2. Object Storage

Start SeaweedFS locally (all-in-one master+volume+filer+S3 gateway):

```bash
docker run -p 8333:8333 -p 8888:8888 -p 9333:9333 \
  chrislusf/seaweedfs server -dir=/data -ip=localhost \
  -s3 -s3.port=8333 -filer -filer.port=8888 -master.port=9333
```

The S3 gateway started this way has no credential enforcement configured, so for local dev any access/secret key pair works; production/Compose/Kubernetes deployments render an `s3.json` identity file with the real `user123`/`password123` credentials (see `ObjectStorage` config below).

The application expects two buckets: `uploaded-images` and `resized-images`. These are created automatically on startup.

#### 3. RabbitMQ

```bash
docker run -p 5672:5672 -p 15672:15672 rabbitmq:management
```

#### 4. API

```bash
cd 3-kubernetes/api/mytravels.api
dotnet run
```

Swagger UI is available at `http://localhost:5101/swagger`.

#### 5. Messaging Worker

```bash
cd 3-kubernetes/messaging/mytravels.messaging
dotnet run
```

## Configuration

### Docker Compose - `.env`

All Docker Compose services read environment variables from the `.env` file in the `3-kubernetes/` directory.

| Variable | Description |
|---|---|
| `CORE_DB_CONTEXT` | PostgreSQL connection string used by the API, messaging worker, and migration |
| `RABBIT_MQ_URI` | RabbitMQ AMQP connection URI |
| `GOOGLE_API_KEY` | Google Maps Geocoding API key |
| `GOOGLE_MAPS_URL` | Google Maps base URL |
| `GOOGLE_PLACES_URL` | Google Places base URL |
| `SEAWEEDFS_ACCESS_KEY` | SeaweedFS S3 access key (also used as `ObjectStorage__AccessKey` inside containers) |
| `SEAWEEDFS_SECRET_KEY` | SeaweedFS S3 secret key (also used as `ObjectStorage__SecretKey` inside containers) |
| `SEAWEEDFS_ENDPOINT` | SeaweedFS S3 endpoint reachable from inside the Compose network (e.g. `seaweedfs:8333`) |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core environment (`Development`, `Production`) |
| `ASPNETCORE_URLS` | Listen URL for the service (e.g. `http://+:5101`) |

### Local development - `appsettings.Development.json`

When running with `dotnet run`, configure `3-kubernetes/api/mytravels.api/appsettings.Development.json`:

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
  "ObjectStorage": {
    "Endpoint": "localhost:8333",
    "AccessKey": "user123",
    "SecretKey": "password123"
  },
  "CorsHosts": "http://localhost:3000"
}
```

## API Reference

Base path: `/api/pointofinterest`

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/pointofinterest?rows=&start=` | List POIs from the SOLR index (capped at `rows`, default 100) |
| `GET` | `/api/pointofinterest/search?term=&rows=&start=` | Search POIs across address, tags and description |
| `GET` | `/api/pointofinterest/pointOfInterestKey/{key}` | Get a specific POI by key |
| `GET` | `/api/pointofinterest/{id}?resizedImage=bool` | Get a POI image as base64 |
| `POST` | `/api/pointofinterest` | Save/update tags on one or more POIs |
| `POST` | `/api/pointofinterest/coordinates` | Create a POI from coordinates |
| `POST` | `/api/pointofinterest/image` | Upload an image (EXIF GPS data extracted automatically) |
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
└── Tags                     many-to-many via PointOfInterestTagAssociation

Tag
└── Name                     unique

PointOfInterestAuditLog      history of address changes
```

**PostgreSQL schemas:**
- `public` - main tables
- `config` - EF Core migrations history

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
docker build -f 3-kubernetes/api/mytravels.api/Dockerfile            -t mytravels-api       .
docker build -f 3-kubernetes/messaging/mytravels.messaging/Dockerfile -t mytravels-messaging .
docker build -f 3-kubernetes/common/mytravels.migration/Dockerfile    -t mytravels-migration .
```

## Ports and Protocols

| Service | Protocol | Local Port | Docker Compose Port | Notes |
|---|---|---|---|---|
| API | HTTP | 5101 | 5101 | Swagger UI at `http://localhost:5101/swagger` |
| Messaging worker | HTTP | 5102 | 5102 | Internal background worker; no public UI |
| PostgreSQL | TCP | 5432 | 5432 | Default PostgreSQL port |
| RabbitMQ | AMQP | 5672 | 5672 | Message broker |
| RabbitMQ Management | HTTP | 15672 | 15672 | Management UI at `http://localhost:15672` |
| SeaweedFS S3 API | HTTP (S3) | 8333 | 8333 | S3-compatible object storage |
| SeaweedFS Filer | HTTP | 8888 | 8888 | Browser UI (plain directory listing, not a full console): `http://localhost:8888` |
