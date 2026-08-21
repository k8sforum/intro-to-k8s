---
description: Batch-upload all photos in a directory as points of interest via the MyTravels API
argument-hint: <directory-path> [api-base-url]
---

Upload every photo in the directory given by `$ARGUMENTS` (non-recursive) as a new point of interest.

Run the upload script in a single Bash call, passing the arguments through unchanged:

```bash
.claude/scripts/upload-photos.sh $ARGUMENTS
```

The second argument is the API base URL and is optional — it defaults to `http://localhost:5101`
(stages 0–2). For stages 3–4 the caller should pass `http://api.mytravels.local:8080`. If the
argument is absent and it is unclear which stage is running, use the default and let the script
report a connection failure rather than guessing.

**Do not read, encode, or otherwise load the image files yourself.** These are 6–16 MB originals;
a single one is several times larger than your entire context window. The script streams each file
from disk to the API with `curl`, so the bytes never enter the conversation. The script also handles
iteration order, per-file error capture, and continuing past failures — there is no reason to loop
over the files yourself or to call the `upload_photo` MCP tool here.

The script prints one TAB-separated record per file (`name`, `OK`/`FAIL`, id or reason) and a final
`TOTAL` line. From that output, print a markdown table with columns **File | Status | Id / Error**,
followed by the one-line passed/failed count.

If every file fails with a connection error, the API is not reachable at that base URL. Diagnose it
inline in the same response — run `curl -sS -m 5 -o /dev/null -w '%{http_code}\n' <api-base-url>/`
and report the result — rather than telling the user to go and check.
