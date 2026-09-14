# Add a search box to the map

**Status:** Ready to implement
**Date:** 2026-09-14

Add SOLR-backed search to the MyTravels web map, move the POI list endpoint onto
SOLR, and retire the `/filter` endpoint.

## API (`src/api/mytravels.api`, `src/common/mytravels.domain`)

1. `GET /api/pointofinterest` (`GetMetadatasAsync`) must read from SOLR instead
   of PostgreSQL. Change `PointOfInterestService.GetAsync` to issue a single
   `*:*` query through `ISolrSearchService` rather than
   `ICoreDbContext.GetAllPointsOfInterestAsync`. Expose `rows` and `start` as
   optional query parameters on the route, defaulting to the controller's
   existing `DefaultRows` of 100. Single query only, no server-side paging loop.
   Leave `GetAllPointsOfInterestAsync` in place, the reindex path still uses it.
2. Delete `GET /api/pointofinterest/filter` and the controller overload behind
   it. Nothing in `src/` calls it, so no caller migration is needed.
3. Leave `GET /api/pointofinterest/search` as it is. The web app calls it.

Accept and do not try to fix: a POI is now absent from the map until the
messaging worker consumes its `index-solr` message, and a library over 100 POIs
is silently truncated. Both are consequences of serving the list from SOLR.

## Web (`src/web/src`)

Add a search control to the map page.

- **Debounce**: 500ms after the last keystroke. Fire only at 4 or more
  characters. Abort the in-flight request on each new keystroke with an
  `AbortSignal`, `client.ts` already threads one through every call.
- **Endpoint**: `GET /api/PointOfInterest/search?term=&rows=&start=`. Add a
  `searchPointsOfInterest` function to `src/web/src/api/client.ts` alongside
  `getPointsOfInterest`. The response shape is `PointOfInterest[]`, unchanged.
- **Result**: the map shows only the search results as pins.
- **Restore rules**: dropping below 4 characters leaves the last result set on
  the map. Pins revert to the full set only when the box is emptied or the panel
  is hidden.
- **Control**: a search button on the map that animates the text box open. The
  open box carries a hide button that collapses it again.

Best practices to apply, given the map is the whole page:

- The control floats over the map. Match the existing floating-control idiom in
  `MapPage.tsx`, which positions `UploadButton` with absolute Tailwind classes at
  `z-[500]` to clear Leaflet's panes. Do not push the map down or resize it.
- Stop click and scroll events on the control from reaching the map underneath,
  otherwise typing near the map zooms it.
- Autofocus the input when it animates open. Escape hides it. Respect
  `prefers-reduced-motion` on the open and close transition.
- Give the input an accessible label, `type="search"`, and announce the result
  count in a live region.
- Show three states distinctly: searching, a result count, and no matches. No
  matches must not read as a broken map, keep a visible message on the empty map.
- `MapView`'s `FitToMarkers` refits the viewport on every change to the `pois`
  array. That gives search auto-fit for free, but it also means the existing
  upload poll yanks the viewport every 3 seconds. Decide deliberately whether
  search results refit and whether the poll should stop doing so.
- `MapPage`'s `pollUntilResolved` compares list lengths before and after an
  upload. Check it still behaves when the list is search-filtered, and when the
  list is served from an index that lags the upload.

## Documentation and stages

- Rewrite the tag-filter cells that curl `/api/pointofinterest/filter` in
  `2-dockerhub/runbook.ipynb`, `3-kubernetes/runbook.ipynb` and
  `4-argocd/runbook.ipynb` to use `/search` instead. Follow the runbook rule: on
  a failed check, run the diagnostic inline in the same cell.
- Update `SPEC.md`: the API table at line 326, the SOLR field notes at 534, the
  known-issues entries at 802 and 803, and the changelog at 847. Note while you
  are there that `SolrSearchQuery` no longer carries `Tag`, `From` or `To`, so
  the SPEC and CLAUDE.md descriptions of `search_pointofinterest` and of
  `tag_exact` serving `/filter` are already stale.
- Update the POI search section of `CLAUDE.md` for the removed endpoint and the
  SOLR-backed list.
- Bump the `api` and `web` image tags in all six places:
  `2-dockerhub/docker-compose.build.yml`, `2-dockerhub/docker-compose.yml`,
  `1-dockerize/docker-compose.yml`, `0-local/docker-compose.yml`, both manifest
  sets, and `src/scripts/merge-manifests.sh`. Do not build or push. Flag in your
  summary that stages 2 to 4 will not pull until the images are built on both
  architectures and merged.

There is no test suite in this repo. Verify with `dotnet build src/mytravels.sln`
and `npm run build` plus `npm run lint` in `src/web`.

## Decisions already made

These were settled when the prompt was written. Do not reopen them.

| Question | Decision |
|---|---|
| List endpoint truncation | Single capped SOLR query, no paging loop, no PostgreSQL fallback |
| Below 4 characters | Last result set stays on the map, only clear or hide restores all pins |
| Blast radius of removing `/filter` | Code, runbooks, SPEC.md and CLAUDE.md |
| Image tags | Bump everywhere, do not build or push |

## Known consequence worth a decision

`tag_exact` in the SOLR schema (`SolrIndexService.Fields`) exists solely to serve
`/filter`, and `SolrSearchService` no longer reads it. This prompt leaves the
field in place, which is the safe call. Dropping it is a separate change.
