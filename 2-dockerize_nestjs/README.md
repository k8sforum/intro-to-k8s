# MyTravels

A geolocation-based Points of Interest (POI) management system built on NestJS. Upload images or drop coordinates to track and tag locations, with asynchronous image resizing and automatic address resolution via Google Maps.

See **[runbook.ipynb](runbook.ipynb)** for step-by-step instructions to start and verify all services locally.

## Architecture

```
apps/
  api/          REST API — HTTP on port 3000
  messaging/    Background worker — RabbitMQ consumer
libs/
  contract/     Shared DTOs, interfaces, and exceptions
  domain/       TypeORM entities and database module (PostgreSQL)
  common/       GeoService, GoogleMapsService, MessagePublisher
  storage/      MinIO object storage service
```

### Request flow

1. Client uploads an image to `POST /api/point-of-interest/image`
2. The `api` app stores the image in MinIO and publishes a message to RabbitMQ
3. The `messaging` worker consumes the message, resizes the image, and reverse-geocodes the coordinates
4. The formatted address is written back to the database

## Ports and protocols

| Service | Docker Port | Development Port | Sample URL | Protocol |
|---|---|---|---|---|
| **mytravels.api** | 3000 | 3000 | http://mytravels.local/api | HTTP |
| **mytravels.messaging** | – | – | – | AMQP consumer |
| **PostgreSQL** | 5432 | – | – | TCP |
| **RabbitMQ** | 5672 | – | – | AMQP |
| **RabbitMQ Management UI** | 15672 | – | http://mytravels.local/rabbitmq | HTTP |
| **MinIO** | 9000 | – | http://mytravels.local/minio | HTTP |
| **MinIO Console** | 9090 | – | http://mytravels.local/minio-ui | HTTP |

## Prerequisites

- Node.js 20+
- PostgreSQL
- RabbitMQ
- MinIO
- A Google Cloud project with the **Maps** and **Places** APIs enabled

## Environment variables

Copy `.env.example` to `.env` and fill in the values:

| Variable | Description |
|---|---|
| `DATABASE_URL` | PostgreSQL connection string — e.g. `postgresql://user:pass@localhost:5432/CoreDb` |
| `POSTGRES_USER` | PostgreSQL username |
| `POSTGRES_PASSWORD` | PostgreSQL password |
| `POSTGRES_DB` | Database name |
| `RABBIT_MQ_URI` | RabbitMQ connection URI — e.g. `amqp://user:pass@localhost:5672` |
| `RABBITMQ_DEFAULT_USER` | RabbitMQ username |
| `RABBITMQ_DEFAULT_PASS` | RabbitMQ password |
| `MINIO_ENDPOINT` | MinIO host and port — e.g. `localhost:9000` |
| `MINIO_ROOT_USER` | MinIO access key |
| `MINIO_ROOT_PASSWORD` | MinIO secret key |
| `GOOGLE_API_KEY` | Google Cloud API key |
| `GOOGLE_MAPS_URL` | Google Maps base URL |
| `GOOGLE_PLACES_URL` | Google Places base URL |
| `PORT` | API listen port (default: `3000`) |
| `CORS_HOSTS` | Semicolon-separated list of allowed CORS origins |
| `NODE_ENV` | Set to `production` to enable security headers |

## API reference

Swagger UI is available at [http://localhost:3000/swagger](http://localhost:3000/swagger) when the API is running.

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/point-of-interest` | List all points of interest |
| `GET` | `/api/point-of-interest/filter?filterString=` | Filter by tag name |
| `POST` | `/api/point-of-interest/image` | Upload an image to create a new POI |
| `PUT` | `/api/point-of-interest?pointOfInterestKey=` | Replace the image of an existing POI |
| `PUT` | `/api/point-of-interest/status` | Update the status of a POI |

All error responses follow the `ApiErrorDto` shape:

```json
{
  "id": "a1b2c3d4e5f6",
  "httpStatusCode": 404,
  "code": "",
  "links": "/api/point-of-interest",
  "title": "Resource not found",
  "message": "Point of interest not found",
  "detail": ""
}
```

## Project structure

```
apps/
  api/src/
    main.ts                           Bootstrap, Swagger setup, CORS, global pipes
    app.module.ts
    middleware/exception.filter.ts    Global exception → ApiErrorDto mapper
    point-of-interest/
      point-of-interest.controller.ts
      point-of-interest.module.ts
  messaging/src/
    main.ts
    app.module.ts
    append-formatted-address.handler.ts
    resize-image.handler.ts
libs/
  contract/src/
    dto/                              Request/response DTOs
    exceptions/                       Typed domain exceptions
    interfaces/                       Service interfaces
    lookups/                          Enums (status, type)
    messages/                         RabbitMQ message payloads
    responses/                        Query result shapes
  domain/src/
    entities/                         TypeORM entities
    point-of-interest/
      point-of-interest.service.ts
    core-db.module.ts
    data-source.ts                    TypeORM DataSource (used by migration script)
  common/src/
    geo.service.ts
    google-maps.service.ts
    message-publisher.service.ts
    message-subscriber.base.ts
  storage/src/
    minio-storage.service.ts
scripts/
  migrate.ts                          Runs TypeORM migrations
```
