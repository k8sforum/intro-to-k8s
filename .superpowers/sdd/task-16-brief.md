# Task 16: Smoke Test the End-to-End Traceability Flow

**Goal:** Verify that the traceability implementation works end-to-end by running the stage 1 stack (Compose with observability) and confirming that traces flow from API through RabbitMQ to Messaging, and that failed messages are published to `-failed` exchanges.

**Files:**
- No files modified; testing only.

**Interfaces (what this task produces for downstream tasks):**
- Confidence that traces flow end-to-end.
- Confirmation that failed messages are published.
- Verification that no infinite requeue loops occur.

**Consumes from earlier tasks:**
- All Tasks 1-15 must be complete and merged.

**Global Constraints:**
- No changes to project code during test (only observation).
- Test environment: Stage 1 (Compose with observability stack).
- Smoke test focus: happy path + one deliberate failure.

**Steps:**

### Step 1: Verify all changes are committed

Before starting the smoke test, ensure all Tasks 1-15 are committed to the current branch:

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git status
```

Expected: No uncommitted changes. All tasks should be on the branch.

### Step 2: Build and start stage 1

Navigate to the stage 1 directory and start the stack:

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/1-dockerize
docker compose up --build
```

Wait for all services to reach a healthy state:
- `api` (port 5101) should be running
- `messaging` (port 5102) should be running
- `web` (port 3000) should be running
- `collector` (OTel Collector) should be running
- `postgres`, `rabbitmq`, `minio` should be running

### Step 3: Trigger a photo upload via the web UI

Navigate to `http://localhost:3000` in a browser (or use the configured web URL).

Create a new Point-of-Interest with a photo:
1. Click "Add POI" or similar button.
2. Fill in Name, Description, Coordinates.
3. Upload a small JPEG or PNG photo.
4. Submit.

### Step 4: Monitor logs for trace flow

In the Docker Compose terminal, watch the logs from `api` and `messaging` services. Look for:

**API logs (on POST /pointofinterest):**
- A trace ID assigned (e.g., from OpenTelemetry middleware).
- Structured log with the created PointOfInterestId and CorrelationId.
- Messages published to RabbitMQ exchanges (resize-image, append-formatted-address, append-image-tags).

**Messaging logs (subscribers):**
- Consumer activity logs showing the same trace ID (or linked via traceparent header).
- Structured logs from ResizeImage, AppendFormattedAddress, AppendImageTags subscribers.
- Logs should include {PointOfInterestId} and {CorrelationId}.
- Success logs: "Image resized", "Address formatted", "Description generated".

**Example log pattern:**
```
[api] TraceId:e1b2c3d4e5f6g7h8, PointOfInterestId:42, CorrelationId:a1b2c3d4-e5f6-g7h8-i9j0-k1l2m3n4o5p6
[messaging] ConsumerActivity {ResizeImage} from {resize-image}, retry count 0
[messaging] Error processing ResizeImage message for POI {PointOfInterestId:42}, correlation {CorrelationId:a1b2c3d4-...}
```

### Step 5: Trigger a deliberate failure (optional but recommended)

To verify the failure path works:

**Option A: Upload a corrupted image**
- Create a file with a `.jpg` extension but containing non-image data.
- Upload it as a photo.
- Watch the messaging logs for the resize-image subscriber to fail.

**Option B: Provide bad coordinates**
- Upload a normal photo but provide coordinates that cause geocoding to fail (e.g., 0.0, 0.0 or invalid format).
- Watch the append-formatted-address subscriber retry 3 times.
- After 3 retries, observe a warning/error log indicating the message was published to `append-formatted-address-failed`.

### Step 6: Verify no infinite requeue

For the failed message scenario:
- Watch the logs for 3 retry attempts (x-retry-count: 1, 2, 3).
- After the 3rd attempt, observe a log indicating the message was dead-lettered to the `-failed` exchange.
- **Critical:** Verify that the message does NOT keep retrying infinitely (no "attempt 4, 5, 6...").

### Step 7: Check trace export (optional)

If Grafana/Tempo is running in stage 1:
- Navigate to Grafana at `http://localhost:3000` (or configured port).
- Search for the trace by the ErrorId or TraceId from the API response.
- Verify the trace shows the full flow: api → RabbitMQ publish → messaging → subscriber.

### Step 8: Cleanup

When smoke test is complete, stop the Compose stack:

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/1-dockerize
docker compose down
```

### Step 9: Commit smoke test results

No code changes are needed, but add a line to the commit log noting the smoke test passed:

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git status  # Should show clean
git log --oneline | head -5  # Show recent commits
```

If everything passed, the smoke test is complete. If issues arise, debug and fix in the appropriate earlier tasks before re-testing.

**Definition of Done:**
- Stage 1 stack builds and runs successfully.
- Photo upload completes and messages are published.
- Trace IDs appear in logs (from API and Messaging).
- Structured logs include {PointOfInterestId} and {CorrelationId}.
- Failure scenario (if tested): 3 retries, then dead-letter, no infinite loop.
- No build or runtime errors.
- Compose stack cleanly shuts down.
