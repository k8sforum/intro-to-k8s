# MCP server for MyTravels — read-only query interface

**Status: implemented.** `src/api/mytravels.mcp` exists, builds, and exposes two query tools (`search_pointofinterest`, `search_place`) over streamable HTTP. This doc records the current design — useful for understanding or extending the MCP server.

A .NET project, `src/api/mytravels.mcp`, sits alongside `mytravels.api` and `mytravels.messaging` in the solution (`src/mytravels.sln`, its own `mcp` solution folder). It exposes an MCP server over **streamable HTTP transport** using the official `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` NuGet SDK (v2.2.0). The server reuses the existing `common`/`domain`/`storage` service layer directly via the same DI pattern as `mytravels.api`/`mytravels.messaging` — not a proxy over REST.

`Program.cs` wires:
- OpenTelemetry (traces/metrics via OTLP exporter)
- RabbitMQ, MinIO, EF Core, PostgreSQL (inherited from domain layer, though MCP tools don't publish/consume messages or write objects)
- `IPointOfInterestService`, `IMapsService` (injected into tool classes)
- `AddMcpServer().WithHttpTransport().WithTools<PointOfInterestMcpTools>().WithTools<PlaceMcpTools>()` (MCP server registration)
- `app.MapMcp()` (HTTP transport endpoint)
- `app.MapGet("/health", ...)` (health check, returns 200)

Listens on port 5103 (after api=5101, messaging=5102).

## Tools exposed

Two tool classes in `src/api/mytravels.mcp/Tools/`:

### PointOfInterestMcpTools
- **`search_pointofinterest`** — searches for existing points of interest by formatted address (substring match). Returns a list of `PointOfInterestDto` with id, coordinates, tags, and metadata. Input: `term` (search string). Throws `McpException` if term is blank.

### PlaceMcpTools
- **`search_place`** — resolves a free-text place name/address into candidate locations with coordinates, for placing a photo without GPS metadata. Delegates to `IMapsService.SearchPlacesAsync()` (Google Maps if key configured, falls back to OpenStreetMap). Returns list of `PlaceDto`. Inputs: `query` (free-text search), `limit` (max results, defaults to 5 if not positive). Throws `McpException` if query is blank.

Tool-level input validation (blank strings) throws `ModelContextProtocol.McpException`. MCP has no HTTP status codes, so this is the equivalent of REST `BadRequest`.

## Design decisions

- **Read-only interface.** The MCP server queries existing data only — no file uploads, photo storage, or side effects. Rationale: MCP tools are single-shot tool calls with no streaming context. Photo upload would require multi-part form data + EXIF parsing + async processing, which doesn't fit the tool paradigm well. Querying existing POIs and resolving place names to coordinates is cleaner.
- **Reuses domain layer directly.** `IPointOfInterestService` and `IMapsService` are called directly, not wrapped in a proxy. This keeps the MCP and REST APIs in sync — a service change flows to both.
- **Auth**: none. Inherits the app's existing anonymous-everywhere posture (SPEC.md F-5).
- **Library**: official `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` SDK — not hand-rolled. The build validates the exact API surface: `AddMcpServer`, `WithHttpTransport`, `WithTools<T>`, `MapMcp`, `[McpServerToolType]`, `[McpServerTool]`, `McpException`.
- **Containerization**: full, integrated into all five stages — `src/api/mytravels.mcp/Dockerfile` (alpine multi-stage, matches `mytravels.api` pattern), service entries in Compose files (`1-dockerize/`, `2-dockerhub/`), build entry in `2-dockerhub/docker-compose.build.yml`, and Kubernetes manifests (`3-kubernetes/manifests/mcp/`, `4-argocd/manifests/mcp/`).

## Environment / configuration

MCP inherits standard MyTravels configuration:
- `CoreDbContext` connection string (PostgreSQL)
- `RabbitMQ:Uri` (required by DI, though unused by MCP tools)
- `MinIO` section (required by DI, though unused by MCP tools)
- `GoogleApiKey` (optional, for Google Maps in `search_place`; falls back to OpenStreetMap if missing)
- `OTEL_*` vars for OpenTelemetry (service name defaults to `mytravels-mcp`)

In Kubernetes (stages 3–4), `GoogleApiKey` is sourced from the `mcp-secret` (created imperatively in stage 4 runbook).
