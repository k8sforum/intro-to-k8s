#!/usr/bin/env python3.9
"""
Parses an executed Jupyter notebook and prints any cell errors to stdout.
Exit code 1 if errors found, 0 if clean.
"""
import json
import sys

if len(sys.argv) < 2:
    print("Usage: check_errors.py <notebook.ipynb>")
    sys.exit(2)

with open(sys.argv[1]) as f:
    nb = json.load(f)

errors = []
for i, cell in enumerate(nb["cells"]):
    if cell["cell_type"] != "code":
        continue
    for output in cell.get("outputs", []):
        if output.get("output_type") == "error":
            source = "".join(cell.get("source", []))[:120]
            errors.append(
                f"Cell {i} | {output['ename']}: {output['evalue']}\n"
                f"  source: {source!r}\n"
                f"  traceback:\n" + "\n".join(f"    {l}" for l in output.get("traceback", []))
            )

if errors:
    print(f"{len(errors)} error(s) found:\n")
    for e in errors:
        print(e)
    sys.exit(1)

print("OK — no errors found.")
sys.exit(0)
