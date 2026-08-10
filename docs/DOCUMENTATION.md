# Documentation Structure Guide

Use this guide to keep a project's Markdown documentation clear, durable, and
free of duplicated sources of truth.

## Goal

Keep the project root small while preserving automatic repository and agent
discovery:

- `README.md` is the project front door.
- `AGENTS.md` contains automatically discovered coding-agent instructions.
- `LICENSE.md` contains the repository license or private-use notice.
- `docs/` contains all other curated project documentation.
- `docs/scratch/` contains rough or historical material that is not source of
  truth.

Keep one authoritative copy of every decision. When reorganizing existing
documentation, preserve useful project information, remove copied-project
references, and update all links.

## PowerCode Canonical-Source Layout

This guide describes the desired layout of consuming repositories. PowerCode
also stores the reusable source material for that layout:

- Exact canonical documents remain under `docs/` at their consuming-project
  target paths.
- Adaptable root-document starters live under `templates/repository/`.
- `canonical-manifest.json` maps template sources to their target paths and
  assigns applicability profiles.
- PowerCode's own root `README.md`, `AGENTS.md`, and `LICENSE.md` describe and
  govern PowerCode itself; they are not starter files.

This separation lets directory comparison expose exact canonical files while
preventing PowerCode-specific root documents from being copied as templates.

## Root Exceptions

The only Markdown files normally kept at the project root are:

```text
README.md
AGENTS.md
LICENSE.md
```

These files remain at the root for repository-host, coding-agent, and license
discovery. This exception is about discoverability, not relative importance.

Do not add root-level design, development, plan, formatting, or topic
documents. Put them under `docs/` and link to them from the root README or
AGENTS file.

## Naming And Document Classes

Use conventional uppercase names for short, prominent project-control documents
directly under `docs/`:

```text
docs/CODEFORMAT.md
docs/DESIGN.md
docs/DEVELOPMENT.md
docs/DOCUMENTATION.md
docs/PLAN.md
docs/WORKFLOW.md
```

Other high-level control documents may use the same convention when their
prominence justifies it, such as `ARCHITECTURE.md`, `CHANGELOG.md`,
`CONTRIBUTING.md`, `SECURITY.md`, or `TESTING.md`.

`WORKFLOW.md` is the conventional uppercase name for the primary repository
workflow. Use lowercase kebab-case for additional workflow documents and
focused feature or system documents:

```text
docs/deployment-workflow.md
docs/features/configuration.md
docs/features/device-matching.md
docs/features/input-routing.md
docs/features/interception-dependency.md
docs/features/profile-actions.md
docs/features/tray-application.md
docs/scratch/2026-05-14-early-design-discussion.md
```

`README.md` remains uppercase inside folders because repository hosts commonly
render it automatically.

## Recommended Layout

```text
README.md
AGENTS.md
LICENSE.md

docs/
  README.md
  CODEFORMAT.md
  DESIGN.md
  DEVELOPMENT.md
  DOCUMENTATION.md
  PLAN.md
  WORKFLOW.md
  features/
    <focused-topic>.md
  scratch/
    README.md
    <dated-historical-note>.md
```

## Document Responsibilities

### Root `README.md`

The project front door.

Include:

- What the project is.
- Main capabilities and maturity.
- A short project layout.
- Quick-start commands or a link to development setup.
- Links to the documentation index and high-level docs.

Avoid detailed setup, raw notes, implementation history, and duplicated plans.

### Root `AGENTS.md`

Concise operational instructions for coding agents.

Include required workflows, safety constraints, testing expectations,
project-specific conventions, and links to broader guidance under `docs/`.
Keep it at the root so repository-scoped agent instructions are discovered
without relying on a manual link.

### Root `LICENSE.md`

The repository's complete license text or an explicit private-use notice. Keep
one authoritative file; do not add a second redirecting license document or
move the notice under `docs/`.

### `docs/README.md`

The complete index for curated project documentation.

Group links by purpose when useful:

- High-level project-control docs.
- Workflow docs.
- Feature and system docs.
- Scratch policy.

Avoid raw transcripts, current task lists, and duplicated focused content.

### `docs/DESIGN.md`

Durable cross-system product and architecture decisions. Include principles,
configuration and data-model rules, code and dependency boundaries, and
constraints future implementations must respect.

### `docs/DEVELOPMENT.md`

How to work on, run, test, configure, and release the project. Include required
tools, external-dependency setup, build and test commands, troubleshooting, and
distribution constraints.

### `docs/PLAN.md`

The committed implementation snapshot and roadmap. Include implemented,
partial, and future work plus useful completed housekeeping. Avoid duplicating
stable decisions from `DESIGN.md`.

### `docs/CODEFORMAT.md`

Formatting and style conventions. Include language formatting, naming rules,
formatter commands, generated-code rules, and Markdown placement rules.
Summarize any safety-critical rule in the root `AGENTS.md`.

### `docs/DOCUMENTATION.md`

This document: the repository's documentation placement, naming, and
source-of-truth policy.

### `docs/WORKFLOW.md`

The primary repository workflow, including checkout ownership, worktree,
branch, integration, and cleanup rules.

### `docs/<workflow>.md`

Durable contributor or automation workflows that apply across features. Use
lowercase kebab-case and index them from `docs/README.md`.

### `docs/features/<focused-topic>.md`

The curated source of truth for one feature, dependency, or subsystem. Include
current decisions, behavior, constraints, and relevant implementation notes.
Avoid full chat transcripts, repeated backlog lists, and stale alternatives
unless they explain an important decision.

### `docs/scratch/`

Reference-only rough or historical material.

Use it for early brainstorming, temporary research, work-in-progress notes, and
historical conversations that retain useful context. Scratch material is not
authoritative unless verified against current code and curated docs.

Use a sortable date prefix for historical material when a known date adds
context:

```text
docs/scratch/2026-05-14-early-design-discussion.md
```

Label historical material clearly and link to any current source of truth.
Promote durable decisions into curated docs and actionable work into the shared
tracker.

## TODO And Working Notes

A project may commit a TODO document as shared state or ignore it as personal
working state. Before linking to or updating one, verify whether it is tracked.

Shared future work normally belongs in `docs/PLAN.md`, an issue tracker, or
another explicitly chosen durable backlog. Do not mention ignored local notes
in curated documentation because they do not exist in a clean checkout.

## Private Repositories And Reusable Documents

A private repository may contain internal process and ownership information,
but privacy is not a substitute for secret handling. Do not commit credentials,
tokens, private keys, customer data, or machine-local secrets to documentation.

Before copying a canonical document into another repository:

1. Confirm the destination's visibility and audience.
2. Remove PowerCode-specific paths and private operational details unless they
   are intentionally required there.
3. Keep reusable guidance project-neutral where practical.
4. Preserve a destination-specific `LICENSE.md`; do not infer licensing from
   PowerCode's private status.

## Restructuring Process

When applying this guide:

1. Inventory root Markdown and `docs/**/*.md` files.
2. Keep only `README.md`, `AGENTS.md`, and `LICENSE.md` at the root.
3. Move short high-level project-control docs into `docs/` with uppercase
   conventional names.
4. Keep the primary `WORKFLOW.md` uppercase; use lowercase kebab-case for
   additional workflow documents.
5. Move focused system docs into `docs/features/`.
6. Move useful raw or historical material into `docs/scratch/`.
7. Preserve durable project information in one authoritative document.
8. Update every relative link using exact filename casing.
9. Search for old paths and copied-project terminology.
10. Run Markdown and repository verification.
11. Summarize and commit the completed reorganization.

## Source-Of-Truth Rules

```text
Is it the first thing a newcomer should read?
  -> README.md

Is it automatically discovered coding-agent guidance?
  -> AGENTS.md

Is it the repository license or private-use notice?
  -> LICENSE.md

Is it a prominent cross-project design or workflow control document?
  -> docs/<UPPERCASE-NAME>.md

Is it a contributor or automation workflow?
  -> docs/<lower-kebab-case>.md

Is it a focused feature, dependency, or subsystem?
  -> docs/features/<lower-kebab-case>.md

Is it temporary, raw, or historical reference material?
  -> docs/scratch/<dated-lower-kebab-case>.md
```

Do not keep the same plan or decision in several files. Choose one source of
truth and replace other copies with short links.

## Link Hygiene

After moving documentation:

- Search for old filenames and paths.
- Search for terminology copied from unrelated projects.
- Update links in `README.md`, `AGENTS.md`, `docs/README.md`, and focused
  documents.
- Prefer relative Markdown links.
- Use exact filename casing.
- Verify that every rendered relative file link resolves.
- Ignore example links inside fenced code blocks during automated link checks.

## Documentation Starters

Use the maintained starter files instead of duplicating templates inside this
guide:

- `templates/repository/README.boilerplate.md` targets root `README.md`.
- `templates/repository/AGENTS.boilerplate.md` targets root `AGENTS.md`.
- `templates/repository/docs/README.md` targets `docs/README.md`.

## Quality Bar

A successful documentation structure leaves the repository in this state:

- The root contains only the main README, agent instructions, and license among
  its Markdown files.
- A newcomer can start at `README.md`.
- A maintainer can find all curated docs through `docs/README.md`.
- A coding agent automatically discovers root `AGENTS.md`.
- High-level project-control docs are prominent and consistently named.
- Workflow and feature docs are organized without crowding the project root.
- Scratch material is clearly historical or exploratory.
- No authoritative plan exists only in scratch material.
- No stale copied-project content remains in curated documentation.
