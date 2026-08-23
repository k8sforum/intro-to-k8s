# Reflect a new part of the stack in `drawio/architecture.drawio`

Use this when you've added something to the project — Kubernetes objects, Argo CD, a new
service, a new observability tool — and you want it drawn into the architecture diagrams.

---

## The prompt

> Read the **"{{SOURCE TAB}}"** tab in `drawio/architecture.drawio`. Study it, then replicate it
> into a new tab **"{{NEW TAB NAME}}"** that additionally reflects **{{WHAT YOU ADDED}}**.
>
> Derive what to draw from the repo, not from memory — read the manifests / compose files /
> `Program.cs` for the components, their ports, and how they actually talk to each other. Tell me
> which files you used.
>
> Constraints:
> - Don't modify the source tab except where I explicitly ask; add a new tab.
> - Dotted rectangle = the tech/container; rounded rectangle = the module it implements. Follow it.
> - Every line must terminate on a shape via a real source/target with explicit exit/entry anchors —
>   no floating endpoints, no lines that stop on a dotted container when a module is what's meant.
> - No two lines may overlap or run collinear, and no line may pass through a shape. Crossings are
>   allowed only where topologically forced — render those with `jumpStyle=arc` and list them for me.
> - Verify the geometry with a script, not by eye. Report the check results.
>
> Before you start: confirm the file on disk is current (see "Check the file is saved" below). If it
> isn't, stop and tell me — don't build on a stale copy.

---

## Check the file is saved (do this first, every time)

draw.io holds unsaved edits in memory. If you generate a tab against a stale file, the user's next
save silently destroys your work — this has already happened once.

```bash
ls -la --time-style=full-iso drawio/architecture.drawio && date
git diff --stat drawio/
```

If the mtime matches your own last write and the user says they edited it, their changes are
unsaved. Stop and ask them to save. Diff the tab against `HEAD` to see what they actually changed:

```bash
git show HEAD:drawio/architecture.drawio > /tmp/old.drawio   # then compare the tab bodies
```

## File facts

- Plain uncompressed XML. One `<diagram name="..." id="...">` per tab, each with its own
  `<mxGraphModel><root>`.
- **Cell IDs are page-scoped**, so a tab can be copied *verbatim* into a new `<diagram>` with no
  re-IDing. Do that — it preserves geometry and the embedded logo PNGs exactly.
- Logos are inline `image=data:image/png,...` data URIs (MinIO, PostgreSQL, RabbitMQ, Google Maps).
  Truncate them when printing the XML or you'll flood your context. There is no offline source for
  new logos — use labelled boxes for anything new rather than inventing an image.
- Some `<mxPoint>` elements have no `x`/`y`. Guard for `None` when parsing geometry.

## Conventions this file uses

| Element | Meaning |
|---|---|
| Dotted rectangle + label/logo | the technology (`ReactJS`, `.NET`, `RabbitMQ`, `MinIO`, `PostgresSQL`, `Prometheus`, …) |
| Rounded rectangle inside it | the module that tech implements (`Web`, `API`, `Resize Image Subscriber`, `Metrics DB`, …) |
| Numbered circles + right-hand legend | a step-by-step workflow overlay |
| Ports | on the module (`API :5101`); container-level ports that aren't a single module's go in a small text note inside the dotted box |

Existing app styling: `fillColor=#eeeeee;strokeColor=#36393d;strokeWidth=3`.
New layers read better in a distinct fill — the observability layer uses
`fillColor=#FFF2CC;strokeColor=#D6B656`.

Edge colours already in use: `#D79B00` telemetry/OTLP, `#9673A6` Prometheus scrape,
`#82B366` Grafana query, `#808080` app data flow. Add a colour key when you add a colour.

## Where the truth lives

| To draw | Read |
|---|---|
| Services, ports, host-port mappings | `1-dockerize/docker-compose.yml` (`2-dockerhub/` for registry images) |
| K8s objects, namespaces, probes | `3-kubernetes/manifests/**` |
| GitOps / sync waves | `4-argocd/manifests/**`, `4-argocd/runbook.ipynb` |
| Ingress hostnames | `3-kubernetes/manifests/9-ingress.yaml` |
| Scrape targets, pipelines | `observability/prometheus.yml`, `otel-collector-config.yaml` |
| App instrumentation | `src/*/Program.cs`, `src/web/src/telemetry.ts` |
| Architecture background | `SPEC.md` §1–2 |

Stages 3 and 4 drift from each other — check both, don't assume 4 is a superset of 3.

## Build it as an idempotent script

Write a generator script rather than hand-editing XML, so it can be re-run after a correction.
It should: strip any previously generated tab (`re.sub` on the tab's `id`), copy the source tab
body, apply changes, splice in the new `<diagram>`, and back up to `.bak` first.

**Gotcha:** if you splice text into the document, any regex match offsets you captured beforehand
are now stale. Re-match after the splice before using `.end()` to insert.

**Gotcha:** values are HTML inside an XML attribute — double-escaped. To render `·` write
`&amp;#183;` in the raw XML. Follow the existing cells (`&amp;nbsp;`).

**Gotcha:** growing a dotted box to fit a new module can silently swallow an unrelated shape —
a workflow step circle, a note. Check for it.

## Routing that doesn't look like spaghetti

Give every edge an explicit waypoint polyline instead of letting the orthogonal router guess.
Reserve a distinct lane per run — each vertical run its own `x`, each horizontal its own `y` —
and never reuse a lane. When several lines converge on one shape, nest them: the line travelling
furthest should be outermost, and lines should enter on different sides.

Anchor both ends: `exitX/exitY` + `entryX/entryY` with real `source`/`target`. Assert that the
declared anchor ratio resolves to the same coordinate as the polyline's endpoint, or the line will
render detached from the shape.

Existing app edges are largely auto-routed, so you can only check your lines against their
*explicit* waypoints. Say so when you report — a crossing with an auto-routed segment won't show up.

## Verify with a script, then report the numbers

Parse the generated tab back and check:

1. **No line through a shape** — each axis-aligned segment vs every non-container shape's bbox
   (4px margin). Exclude the dotted containers; edges legitimately exit them. Exclude each edge's
   own source/target.
2. **No collinear overlap** — pairwise across new lines, and against existing edges' waypoints.
3. **Crossings** — count them, name them, justify any that remain.
4. **Endpoints anchored** — every new edge has source, target, `exitX`, `entryX`, and no stray
   `<mxPoint>` fallback in its geometry.
5. **Anchor matches polyline** — declared exit/entry resolves to the polyline endpoint (±1px).
6. **New shapes don't overlap existing ones** — catches boxes grown to fit new modules.

Then confirm the whole file still parses, list the tabs, and check `git diff --stat` shows only
the additions you intended.
