# Task 15: Update Architecture Diagram (drawio/architecture.drawio)

**Goal:** Update the draw.io architecture diagram to show the new MyTravels.RabbitMQ tracing spans and the three `-failed` exchanges for failed message handling.

**Files:**
- Modify: `drawio/architecture.drawio`

**Interfaces (what this task produces for downstream tasks):**
- Diagram shows MyTravels.RabbitMQ spans in the observability flow.
- Diagram shows the three `-failed` exchanges as fanout endpoints off each subscriber.

**Consumes from earlier tasks:**
- No dependencies; visual documentation only.

**Global Constraints:**
- No new tools or dependencies required.
- Edits should be made in draw.io or a compatible editor.
- Keep existing diagram content and add/update only the relevant sections.

**Steps:**

### Step 1: Open the diagram in draw.io

Open `drawio/architecture.drawio` using draw.io (online) or a compatible desktop application.

Alternatively, if you have direct file editing, you can edit the XML, but using the visual tool is preferred.

### Step 2: Update Monitoring page

Navigate to the "Monitoring" page in the diagram.

Locate the arrows showing OTLP/telemetry export from `api` and `messaging` services to the observability stack (Collector, Prometheus, Tempo, Grafana, etc.).

**Add or enhance** these arrows/boxes to indicate that they now carry `MyTravels.RabbitMQ` Producer/Consumer spans in addition to existing spans:

- Add a label or note to the OTel export flow indicating: "OTLP: ASP.NET, HttpClient, Npgsql, MyTravels.RabbitMQ"
- Or add a small box/label showing "MyTravels.RabbitMQ spans (Producer/Consumer)" near the observability export arrows.

### Step 3: Update Application Architecture page

Navigate to the "Application Architecture" page.

Locate the boxes representing the three message subscribers:
- ResizeImage
- AppendFormattedAddress
- AppendImageTags

**For each subscriber box**, add small fanout-exchange icons (or small rectangles labeled `-failed`) to show the failure path:

- Off the ResizeImage box, add a small fanout icon labeled "resize-image-failed"
- Off the AppendFormattedAddress box, add a small fanout icon labeled "append-formatted-address-failed"
- Off the AppendImageTags box, add a small fanout icon labeled "append-image-tags-failed"

These exchanges should be drawn as dead-end fanouts (no queues bound) to indicate they are optional/future monitoring points.

### Step 4: Save the file

Save the diagram file. Draw.io will save in `.drawio` XML format.

### Step 5: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add drawio/architecture.drawio
git commit -m "docs: update architecture diagrams for traceability spans and failed exchanges"
```

**Definition of Done:**
- Monitoring page updated to show MyTravels.RabbitMQ spans in the OTLP export.
- Application Architecture page updated to show three `-failed` fanout exchanges.
- Diagram remains valid and opens correctly in draw.io.
- Changes committed with message provided above.
