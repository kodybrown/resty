# Agent Notes

## Primary Checkout

- Use `C:\Users\kodyb\Projects\Resty\Resty` on `develop` as the primary
  human and agent checkout for ordinary implementation, documentation, and
  read-only work. `main` is the release branch.
- Verify the actual branch and working-tree status before editing. Preserve
  unrelated local changes. If the branch or project instructions conflict with
  this layout, stop and ask rather than switching or stashing automatically.
- Create task worktrees only when explicitly requested. Resolve the primary
  checkout from Git metadata and use `<project>/worktrees/<short-name>`, based
  on `develop`, with `feature/`, `bugfix/`, `hotfix/`, or `release/` prefixes.
  Do not add `ai` or `codex` identifiers to new names.
- Preserve existing branch names and worktrees. Integration, cleanup, pushing,
  and publication require explicit authorization. Global worktree-location
  rules take precedence; ask the user before affected writes on conflicts.

## Repository Role

Resty is a .NET command-line REST API testing tool. It discovers `.resty` and
`.rest` files, executes or validates the HTTP tests they define, and emits
human-readable, CI-friendly, or agent-friendly output.

## Repository Safety

- Preserve unrelated user changes throughout the task.
- Check current status before broad edits, generated output, or cleanup.
- Commit only scoped, verified work under the user's standing authorization;
  never include unrelated local edits.
- Do not initialize Git, create branches, relocate worktrees, discard changes,
  switch branches, integrate, or push unless the user explicitly requests it.
- Treat files under `artifacts/`, `publish/`, `builds/`, `bin/`, and `obj/` as
  generated output.

## Canonical Synchronization

The canonical source is `~\Projects\PowerCode`.

Before updating Resty from the canonical source:

1. Read `canonical-manifest.json`.
2. Select only the profiles relevant to Resty.
3. Replace `replace` files only when Resty follows that shared standard.
4. Merge `adapt` files while preserving Resty-specific paths, project names,
   install behavior, commands, and ownership boundaries.
5. Treat Resty's adapted copy as authoritative afterward.
6. Do not update PowerCode while updating Resty unless the user explicitly asks
   for both changes.

## Build And Verification

Use the root Makefile for normal workflows:

- `make build`
- `make test`
- `make verify`
- `make run ARGS=...`
- `make publish`
- `make package`
- `make install-user`

On Windows, `make install` and `make uninstall` intentionally do nothing.
Use `make install-user` to copy the published executable to `%USERPROFILE%\Bin`.
On Linux/macOS, `make install` and `make uninstall` operate on
`$(DESTDIR)$(PREFIX)/bin`, and `make install-user` uses `~/.local/bin`.

## Formatting And Docs

- Follow `docs/CODEFORMAT.md`.
- Keep root `README.md`, `AGENTS.md`, and `LICENSE.md` present.
- Prefer focused edits matching nearby style.
- Avoid broad formatting churn unless the task is explicitly formatting.
- Run `make format-check` or `make verify` when changing shared style, docs, or
  build configuration.
