# Task 14: Update Documentation (CLAUDE.md, SPEC.md)

**Goal:** Remove or correct stale references to non-existent MCP upload tools (`upload_photo`, `upload_photo_with_coordinates`) from project documentation.

**Files:**
- Modify: `CLAUDE.md` (project root)
- Modify: `SPEC.md` (if it exists and contains stale claims)

**Interfaces (what this task produces for downstream tasks):**
- Accurate documentation stating MCP exposes only `search_pointofinterest` and `search_place` tools.
- Removed/corrected any claims that upload tools exist.

**Consumes from earlier tasks:**
- No external dependencies; documentation-only task.

**Global Constraints:**
- Spec states MCP currently exposes: `search_pointofinterest` and `search_place` (verified in code).
- No upload tools exist in the MCP implementation today.
- Documentation must be accurate (no aspirational features).

**Steps:**

### Step 1: Review CLAUDE.md

Open `CLAUDE.md` at the repo root.

Search for references to MCP upload tools (e.g., "upload_photo", "upload_photo_with_coordinates", "MCP.*upload").

If found, remove or correct the text to state:

> **Note:** `mytravels.mcp` currently exposes two MCP tools: `search_pointofinterest` (search saved POIs by formatted address) and `search_place` (place lookup for photos with no GPS metadata). Photo upload via MCP is not yet implemented; POI creation/upload is REST-only, driven from the `api` service.

### Step 2: Search SPEC.md (if it exists)

Open `SPEC.md` at the repo root.

Search for similar references to MCP upload tools. If found, apply the same correction as Step 1.

### Step 3: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add CLAUDE.md SPEC.md
git commit -m "docs: correct stale MCP upload tool references"
```

(If SPEC.md doesn't exist or has no stale claims, only commit CLAUDE.md.)

**Definition of Done:**
- Stale MCP upload tool references removed/corrected in CLAUDE.md.
- Stale MCP upload tool references removed/corrected in SPEC.md (if present).
- Documentation accurately states MCP tools: search_pointofinterest, search_place.
- No remaining aspirational or incorrect claims.
- Changes committed with message provided above.
