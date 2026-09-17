Generate a React conventions rules file for `src/web/`.

Output a single file with YAML frontmatter:
---
description: React/TypeScript conventions for MyTravels' web app
globs: src/web/src/**/*.{ts,tsx}
alwaysApply: false
---

Derive every rule by reading the actual code in `src/web/src/` - do not
state generic React best practices. If fewer than 3 files agree on a
pattern, or files actively disagree (e.g. quote style), leave it out
rather than inventing a preference. Do not restate anything already
enforced by `src/web/.oxlintrc.json` (currently `react/rules-of-hooks`,
`react/only-export-components`) - just note that oxlint covers hooks
rules and move on.

Cover, grounded in concrete examples from the code:
- Component declaration style (function form, export style)
- Where/how props are typed (interface naming, placement, destructuring)
- How multi-state UI (loading/error/success) is modeled
- Import conventions, including anything compiler-enforced via
  tsconfig (check `verbatimModuleSyntax` and similar flags)
- Handler/callback naming conventions
- Constant naming/placement for config values and magic numbers
- Where API calls live and their shape (hooks vs plain functions,
  error handling, response typing)
- Fetch cancellation pattern in effects, if one exists
- Comment discipline (when comments are/aren't used)
- Styling approach (utility classes vs CSS modules vs other, and how
  dark mode is handled if applicable)
- File naming conventions
- Default vs named exports
- State management approach (local state, lifting, or a library)

Write it in the same voice as this repo's `prompts/*.md` files: plain
prose and bullet lists, no persona framing, no filler ("As a React
expert..."), one rule per line where possible so each is independently
checkable against a diff.

Rules apply uniformly to `.ts` and `.tsx` files - don't scope any rule
to component files only unless the underlying pattern genuinely doesn't
apply to plain `.ts` modules (e.g. JSX-specific rules).
