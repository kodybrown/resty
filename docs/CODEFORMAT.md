# Code Format

These are the formatting and code-generation rules for the project. The authoritative
machine-readable style lives in `.editorconfig`; when this file and local
tooling disagree, follow `.editorconfig` and existing nearby code.

## First Principles

- Check the current worktree before editing files.
- Read nearby code before changing style-sensitive files.
- Preserve unrelated user work and avoid broad formatting churn.
- Prefer focused edits that match existing project patterns.
- Use generated code or framework conventions only when they are already part of the project.

## Project-Wide Defaults

Use these defaults unless `.editorconfig`, a formatter, or existing files in the repository clearly say otherwise:

- Indent with 2 spaces.
- Always use braces on control blocks; never use single-line control bodies.
- Keep brace placement consistent with the repository.
- Private fields use `_camelCase`.
- Const fields use `PascalCase`, unless the local project uses another convention.
- Methods, properties, types, and public members use `PascalCase`.
- Local variables and parameters use `camelCase`.
- Avoid hardcoding values that belong in configuration, data files, or named constants.

## C# Style

- Target modern C# when the project supports it.
- Keep nullable annotations enabled when the project enables them; do not weaken null-safety to silence warnings.
- Use `var` only when the type is obvious from the right-hand side.
- Prefer string interpolation and `nameof()`.
- Prefer pattern matching where it improves clarity.
- Prefer null-coalescing and null-conditional operators where they improve clarity.
- Use async/await for asynchronous code.
- Use namespaces that match folder structure.
- Prefer file-scoped namespaces when the project uses them.
- Order `using` directives according to local rules; if no local rule exists, put `System.*` first, then third-party namespaces, then project namespaces.
- Keep method-call spacing consistent with the project. If no local rule exists, use normal C# spacing: `method(arg1, arg2)`.

## Code Generation Best Practices

- Follow patterns in existing code before introducing new abstractions.
- Put new code in the appropriate project, module, namespace, and folder.
- Prefer small, composable types and methods over large procedural blocks.
- Add comments only when they explain non-obvious intent, constraints, or tradeoffs.
- Do not add dependencies, frameworks, or architectural layers unless they are justified by the task.
- Update relevant docs, examples, schemas, fixtures, or config when behavior changes.
- Test generated code with the repository's normal build, test, lint, or formatting workflow when practical.

## JavaScript, Vue, And SCSS Style

- Use 2-space indentation.
- Use single quotes in JavaScript and Vue script blocks.
- Follow the repository's `.prettierrc` for semicolons and trailing commas.
- Keep one SCSS selector per rule; do not combine selectors solely to share declarations.
- Treat `.prettierrc` as the machine-readable frontend formatting configuration.

## Markdown And Docs Style

- Keep the main `README.md`, `AGENTS.md`, and `LICENSE.md` at the project root.
- Use uppercase conventional names for short high-level documents directly under
  `docs/`, such as `DESIGN.md`, `DEVELOPMENT.md`, `PLAN.md`, and the primary
  `WORKFLOW.md`.
- Use lowercase kebab-case for additional workflow documents and files under
  `docs/features/` and `docs/scratch/`.
- Prefer relative Markdown links for repository files.
- Keep durable decisions in curated docs under `docs/`.
- Keep raw notes, temporary work files, and historical chats under `docs/scratch/`.
- Do not duplicate the same plan in multiple docs; keep one source of truth and link to it.

## Tests, Fixtures, And Generated Output

- Expected fixtures should represent intended behavior, not incidental output from a temporary bug.
- Regenerate expected output only after deciding and documenting the behavior being blessed.
- Preserve fixture readability and review fixture diffs carefully.
- Avoid committing generated churn that is unrelated to the task.

## Formatting Commands

Use the root Makefile:

```powershell
make format
```

The canonical formatting module discovers repository capabilities instead of
requiring every language tool everywhere:

- It runs `dotnet format` only when a `.sln` or `.slnx` file exists and excludes
  every source directory named `Migrations`.
- It runs the root `package.json` `format:web` script only when that script is
  declared.
- It runs `cargo fmt` only when a `Cargo.toml` file exists.
- It normalizes source and documentation to LF, keeps `.bat` and `.cmd` files
  on CRLF, and skips dependency, generated-output, and test-report directories.

Projects may add a `format-check:web` script to participate in
`make format-check` without rewriting web files. They may extend generated
directory exclusions and LF file patterns with `FORMAT_FIND_PRUNE_EXTRA`,
`FIXEOL_EXCLUDE_ARGS_EXTRA`, and `FIXEOL_LF_PATTERNS_EXTRA` in the root
Makefile.

If the formatter changes many unrelated files, stop and inspect the diff
before continuing.

## Commit Message Guidelines

When committing changes to the repository, follow these guidelines:

- Use the present tense ("Add feature" not "Added feature").
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...").
- Limit the first line to 72 characters or less.
- Separate subject from body with a blank line when a body is needed.
- Use the body to explain what and why, not just how.
- Reference issues and pull requests when relevant.
- Use bullet points and lists when they make the message easier to scan.
- Be concise but descriptive.
- Avoid using "I" or "we" in commit messages.
- Proofread messages for clarity and typos.
