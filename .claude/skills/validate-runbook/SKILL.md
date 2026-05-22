---
name: validate-runbook
description: Validate a Jupyter runbook by executing it and fixing any errors. Use when the user asks to "validate runbook", "run the runbook", "check the runbook", or "fix runbook errors". Accepts a path to a .ipynb file as the argument.
version: 1.0.0
disable-model-invocation: true
allowed-tools: Read, Edit, Bash
---

# Validate Runbook

Execute the notebook at `$ARGUMENTS` and fix any errors found until the runbook runs cleanly.

## Environment

- Python: `/Library/Developer/CommandLineTools/usr/bin/python3.9` (Python 3.9.6)
- Kernel: `python3.9`
- Execute command: `jupyter nbconvert --to notebook --execute --ExecutePreprocessor.kernel_name=python3.9 --ExecutePreprocessor.timeout=300 --output <executed.ipynb> <input.ipynb>`
- Error checker script: [scripts/check_errors.py](scripts/check_errors.py)

## Steps

1. **Resolve the notebook path** from `$ARGUMENTS`. If relative, resolve from the current working directory. If none is given, look for `runbook.ipynb` in the current directory.

2. **Execute the notebook** from the notebook's own directory (so relative paths like `.env` and `docker-compose.yml` resolve correctly):
   ```bash
   cd <notebook-dir>
   jupyter nbconvert --to notebook --execute \
     --ExecutePreprocessor.kernel_name=python3.9 \
     --ExecutePreprocessor.timeout=300 \
     --output _runbook_executed.ipynb \
     <notebook-filename>
   ```

3. **Check for errors** using the bundled script:
   ```bash
   /Library/Developer/CommandLineTools/usr/bin/python3.9 \
     <skill-dir>/scripts/check_errors.py \
     <notebook-dir>/_runbook_executed.ipynb
   ```

4. **If errors are found**, read the full error details from the script output, then:
   - Read the failing cell's source from the original notebook
   - Fix the issue directly in the notebook file (Edit the `.ipynb` JSON) or in the referenced source code
   - Runbook rules to follow when fixing cells:
     - If a health check returns NOT READY, add inline diagnostic commands (e.g. `docker compose logs <service> --tail=30`) to the same cell — never just print a suggestion to run a command separately
     - Do not refactor generated files (`bin/`, `obj/`, `Migrations/`)
   - Go back to step 2 and re-execute

5. **When no errors remain**, delete `_runbook_executed.ipynb` and report which cells were fixed (if any).

## Notes

- Run `jupyter nbconvert` from the notebook's directory so `.env`, `docker-compose.yml`, and other relative paths resolve correctly.
- Execution timeout is 300 s per cell; increase `--ExecutePreprocessor.timeout` if a long-running cell (e.g. `docker compose up -d`) is timing out.
- The `%%bash` magic cells are run by the `bash` kernel magic in IPython — they require the `bash_kernel` package or IPython's built-in `%%bash` cell magic (available in any IPython/Python kernel).
