---
name: animate-drawio
description: Generate a stepped animated GIF and SVG from one page (tab) of a .drawio file. Use when the user asks to "animate a diagram", "make the draw.io lines animated", or "generate the GIF/SVG from a draw.io page". Arguments - the page name, optionally the .drawio file and the step order.
version: 1.0.0
disable-model-invocation: true
allowed-tools: Read, Bash
---

# Animate a draw.io page

Turn one page of a `.drawio` file into `<name>.gif` (for notebooks, which freeze SVG animation) and `<name>.svg` (flowing dashes in a browser). The step engine is `scripts/animate_drawio.py` in this skill's directory. Do not hand-roll the export.

`$ARGUMENTS` is `<page name> [file] [steps]`. Defaults: file is `drawio/core objects.drawio`; output goes to `labs/<lab>/images/` named after the page (ask which lab if unclear). Existing PNGs are never overwritten.

## Steps

1. **Prerequisites.** The draw.io CLI (`/Applications/draw.io.app`, else `brew install --cask drawio`) and Pillow (`pip3 install --user pillow`). Tell the user before installing anything.
2. **List the edges:** `python3 .claude/skills/animate-drawio/scripts/animate_drawio.py "<file>" "<page>" --list`. Each line is an edge id, its source and target, with coordinates and colour.
3. **Get the step order.** If the user gave no steps, propose one from the diagram (read the PNG for the page if present) and confirm it. Edges not in any step are dimmed when they share the step edges' colour; the same edge may appear in several steps.
4. **Generate:**
   ```bash
   python3 .claude/skills/animate-drawio/scripts/animate_drawio.py "<file>" "<page>" \
     --steps "28;12;6;6,26,15,22;8" --id-prefix "<common id prefix>" \
     --out-dir "labs/<lab>/images" --name "<name>" --seconds 3
   ```
   Steps are separated by `;`, edges within a step by `,`. `--seconds` is the time per step (default 3).
5. **Check.** The script prints the temp dir holding `s<N>.png` frames; Read at least one and confirm the right arrows are lit. State plainly that animation playback itself was not viewed.
6. **Report** the files, step table and loop length. Do not edit the notebook or `.drawio`; offer the markdown line `![alt](images/<name>.gif)` (spaces as `%20`).

## Known behaviour

- The SVG animation is CSS; VS Code/Jupyter may freeze it, so point notebooks at the GIF.
- A mislabelled edge is usually a source/target mix-up: re-run `--list` and check coordinates, not guesses.
