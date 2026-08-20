# Add an MCP interface for photo upload to MyTravels

**Status: implemented.** `src/api/mytravels.mcp` exists, builds, and its three tools have been smoke-tested over streamable HTTP (`tools/list` returns all three with correct schemas; `/health` returns 200). This doc now records the shipped design and the decisions behind it, rather than posing them as open questions — useful if this ever needs to be re-derived, extended, or replicated in another branch.

A new .NET project, `src/api/mytravels.mcp`, sits alongside `mytravels.api` and `mytravels.messaging` in the solution (`src/mytravels.sln`, its own `mcp` solution folder — mirrors how `messaging` got its own folder despite being nested under `src/api` on disk). It exposes an MCP server over **streamable HTTP transport** using the official `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` NuGet SDK (v2.2.0), reusing the existing `common`/`domain`/`storage` service layer directly via the same DI pattern as `mytravels.api`/`mytravels.messaging` — not a proxy over REST. `Program.cs` mirrors `mytravels.api`'s DI wiring (OpenTelemetry, RabbitMQ, MinIO, EF Core, `IPointOfInterestService`, `IMapsService`) minus everything HTTP-controller-specific (no Swagger, no CORS, no `AddControllers`), plus `AddMcpServer().WithHttpTransport().WithTools<T>()` and `app.MapMcp()`. Listens on port 5103 (next free after api=5101, messaging=5102).

## Tools exposed (`src/api/mytravels.mcp/Tools/`)

1. `upload_photo` — takes the image content **inline as base64** (`fileContentBase64` + `fileName`), decoded into an in-memory `FormFile` and passed to `PointOfInterestService.SaveFileAsPointOfInsterestAsync` unchanged, mirroring the EXIF-GPS flow of `POST /api/PointOfInterest/image`.
2. `upload_photo_with_coordinates` — same inline-file input, plus explicit `latitude`/`longitude`/`formattedAddress`, validated against `SaveCoordinatesDto`'s existing `[Range]`/`[Required]` attributes. Mirrors `POST /api/PointOfInterest/image/coordinates`, for the no-GPS-EXIF path.
3. `search_place` — mirrors `GET /api/place?query=&limit=` via `IMapsService.SearchPlacesAsync`, so a subagent can look up coordinates for a named place before calling tool 2.

Tool-level input errors (missing/invalid base64, blank query, failed DTO validation) throw `ModelContextProtocol.McpException`; the "no GPS metadata" case from the domain layer is caught and re-thrown the same way. MCP has no HTTP status codes, so this is the closest equivalent to the controllers' `RequiredParameterNotFoundException`/`ModelState` checks.

## Decisions settled during implementation

- **File input is inline, not a path.** The MCP server has no filesystem access — no shared volume, no path-based tool contract. This was a deliberate call (over the alternative of a shared mount between the MCP container and whatever writes the photo) to keep the container's blast radius to exactly what a tool call hands it.
- **Auth**: none. Inherits the app's existing anonymous-everywhere posture (SPEC.md F-5) — there's no auth scaffolding anywhere in the codebase to hook into, and adding one just for this endpoint would be new scope, not scope-matching.
- **Library**: official `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` SDK, not hand-rolled — confirmed nothing in the repo referenced it before this, and the build + smoke test now validate the exact API surface used (`AddMcpServer`, `WithHttpTransport`, `WithTools<T>`, `MapMcp`, `[McpServerToolType]`, `[McpServerTool]`, `McpException`).
- **Containerization**: full, done up front rather than deferred — `src/api/mytravels.mcp/Dockerfile` (alpine multi-stage, matches `mytravels.api`'s pattern; safe since mcp doesn't depend on Magick.NET), `mcp` service entries in `1-dockerize/docker-compose.yml` and `2-dockerhub/docker-compose.yml`, an entry in `2-dockerhub/docker-compose.build.yml` and `src/scripts/merge-manifests.sh` (the actual build/push/multi-arch-merge pipeline — not in the original ask but required for the image to exist at all), and manifests in `3-kubernetes/manifests/mcp/` + `4-argocd/manifests/mcp/` (including the `mcp-secret` entry added to `4-argocd/runbook.ipynb` Step 9, since stage 4 creates secrets imperatively rather than committing them).

## Adjacent fix (same session, different scope)

SPEC.md and the ArgoCD runbook both claimed `.gitignore` ends with `*secret.yaml`, making `3-kubernetes/manifests/*/1-secret.yaml` gitignored — it didn't, and all six existing secret files were tracked in git with dev-default creds. Restored the pattern and `git rm --cached` the six files (values are placeholder `user123`/`password123` creds, so no rotation was needed). The new `mcp` secret file now follows the documented convention: present on disk for local `kubectl apply`, not committed.

## Known follow-ups (not yet done)

- No end-to-end verification against live Postgres/RabbitMQ/MinIO yet (an actual `upload_photo` call resulting in a `PointOfInterest` row) — the tool schemas are confirmed correct, but the domain-layer round-trip isn't.
- No containerized or k8s smoke test yet (`docker compose up --build mcp`, `kubectl apply -f 3-kubernetes/manifests/mcp/`) — see the verification steps in the implementation plan for the exact commands.
- `GoogleApiKey` is dropped from all k8s manifests today (pre-existing SPEC F-14 gap, inherited unchanged) — `search_place` will silently fall back to OpenStreetMap geocoding in k8s, same as `/api/place` already does.
