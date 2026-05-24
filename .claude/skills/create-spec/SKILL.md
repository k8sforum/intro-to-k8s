---
name: create-spec
description: Create a comprehensive, reverse-engineered technical specification for the current project — detailed enough to drive automated test generation and a full rewrite in another language. Use when the user asks to "create a spec", "generate a technical specification", "document the system", or "reverse-engineer the project".
version: 1.0.0
disable-model-invocation: true
allowed-tools: Read, Write, Edit, Bash, Glob, Grep
---

# Create Technical Specification

Produce a comprehensive technical specification for the current project that documents the system's actual behaviour as the source of truth.

## Goal

The specification must be detailed enough to:

- Serve as the foundation for automated unit and integration tests
- Support a future full rewrite of the application in another programming language
- Document current system behaviour as accurately as possible

## Output

Write the specification to `SPEC.md` at the project root (or `$ARGUMENTS` if a path is provided). If `SPEC.md` already exists, ask before overwriting.

The document should read like a reverse-engineered engineering specification written for a team that must reproduce the system exactly in a different technology stack.

## Required Sections

Include all of the following, organised with clear headings:

1. **Application architecture and component relationships** — high-level structure, how parts fit together, dependency direction
2. **Module and service responsibilities** — what each module/service owns
3. **Public interfaces, APIs, inputs, outputs, and side effects** — every endpoint/CLI/exported function with full signatures
4. **Business rules and workflows** — the rules the system enforces and the flows it implements
5. **Data models, schemas, and validation rules** — entity definitions, field types, constraints, relationships
6. **State management and persistence behaviour** — what is stored where, transactions, lifecycle
7. **Configuration handling and environment dependencies** — env vars, config files, defaults, required vs optional
8. **Authentication, authorization, and security-related behaviour** — auth flows, permissions, token handling, secrets
9. **Error handling and retry behaviour** — error types, propagation, retries, fallbacks
10. **Concurrency, async processing, and scheduling behaviour** — where applicable
11. **External integrations and third-party dependencies** — APIs, services, libraries that materially shape behaviour
12. **Expected runtime behaviour and edge cases** — including examples and execution flows where useful
13. **Assumptions, implicit behaviour, and undocumented conventions** discovered in the codebase

## Findings Section

In addition to the spec proper, include a clearly separated **Findings** section that calls out:

- Bugs, inconsistencies, dead code, and ambiguous logic
- Areas where the implementation differs from the apparent intended behaviour
- Risky or fragile logic that could affect a rewrite
- Undocumented behaviour worth flagging

Each finding should reference the file and line (e.g. `src/foo.ts:42`).

## Constraints

- **Ignore the entire `devops` directory and all contents within it.** Do not read, reference, or document anything inside it.
- **Do not rewrite or refactor the application.** This skill is read-only on the source code (except for writing the spec file itself).
- **Do not propose architectural improvements** unless they expose inconsistencies or unclear behaviour relevant to the spec.
- **Treat the existing implementation as the source of truth**, even if parts appear poorly designed.
- **Prioritize precision, completeness, and behavioural accuracy over brevity.**
- **Clearly separate observed behaviour from inferred intent.** Use phrasing like "Observed:" vs "Inferred:" when intent is not explicit in code or comments.

## Steps

1. **Determine output path** from `$ARGUMENTS` (default: `SPEC.md` at project root). If the file exists, confirm with the user before overwriting.

2. **Survey the project structure** using `Glob` / `Bash ls` / `Read`. Build a mental map of:
   - Entry points (main, server bootstrap, CLI)
   - Top-level directories and their roles
   - Build/package manifests (`package.json`, `pyproject.toml`, `*.csproj`, `go.mod`, etc.)
   - Configuration files (excluding `devops/`)

3. **Read source code systematically.** Cover every module that affects runtime behaviour. For large codebases, work directory-by-directory and keep a checklist so nothing is skipped. Do NOT enter the `devops` directory.

4. **Trace key flows end-to-end.** Pick the main user-facing operations and follow them through every layer (controller → service → repository → DB, or equivalent). Document the actual sequence, not the idealised one.

5. **Extract data models** from ORM entities, schema files, migrations, or type definitions. Record field names exactly as they appear in storage (column names, JSON keys), since a rewrite must match.

6. **Document configuration** by grepping for env var reads (`process.env`, `os.getenv`, `ConfigurationManager`, etc.) and config-file loads. List defaults and required vs optional.

7. **Capture error handling** by locating try/catch blocks, error middleware, custom exception types, and retry logic.

8. **Note assumptions and implicit conventions** — anything that "just works" because of a naming convention, file location, or unwritten rule.

9. **Write the spec** to the output path in a single pass, organised by the required sections above. Use Markdown. Use code blocks for signatures, schemas, and example payloads. Reference source locations with `path:line` so a reader can verify.

10. **Add the Findings section** at the end with the issues discovered during analysis.

11. **Report back** to the user with the output path and a one-paragraph summary of what was covered and any major findings.

## Notes

- If the project is large enough that a single spec file would be unwieldy, split into `SPEC.md` (overview + index) plus `SPEC/*.md` per major area, but only do this if the user agrees.
- Prefer exact quotes/signatures from the code over paraphrased descriptions — a rewrite team needs the precise contract.
- When something is genuinely ambiguous, say so explicitly rather than guessing.
