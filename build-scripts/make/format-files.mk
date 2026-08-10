# Canonical repository-wide language and EOL formatting targets.
# Configure REPO_ROOT and EOL ('lf' or 'crlf') before including.

ifndef CANONICAL_FORMAT_FILES_MK_INCLUDED
CANONICAL_FORMAT_FILES_MK_INCLUDED := 1

.PHONY: require-format-eol format format-check

DOTNET ?= dotnet
CARGO ?= cargo
FIXEOL ?= fixeol

ifeq ($(OS),Windows_NT)
	NPM := npm.cmd
else
	NPM ?= npm
endif

FORMAT_FIND_PRUNE_EXTRA ?=
FORMAT_FIND_PRUNE ?= \
	-name .git -o \
	-name node_modules -o \
	-name bin -o \
	-name obj -o \
	-name dist -o \
	-name artifacts -o \
	-name target -o \
	-name coverage -o \
	-name playwright-report -o \
	-name test-results -o \
	-name TestResults
FORMAT_FIND_PRUNE += $(FORMAT_FIND_PRUNE_EXTRA)

FIXEOL_EXCLUDE_ARGS_EXTRA ?=
FIXEOL_EXCLUDE_ARGS ?= \
	--exclude "*/.git/*" \
	--exclude "*/node_modules/*" \
	--exclude "*/bin/*" \
	--exclude "*/obj/*" \
	--exclude "*/dist/*" \
	--exclude "*/artifacts/*" \
	--exclude "*/target/*" \
	--exclude "*/coverage/*" \
	--exclude "*/playwright-report/*" \
	--exclude "*/test-results/*" \
	--exclude "*/TestResults/*" \
	--exclude ".mcp_file_state.json"
FIXEOL_EXCLUDE_ARGS += $(FIXEOL_EXCLUDE_ARGS_EXTRA)

FIXEOL_LF_PATTERNS_EXTRA ?=
FIXEOL_LF_PATTERNS ?= \
	"*.code-workspace" "*.cs" "*.csproj" "*.css" "*.editorconfig" \
	"*.gitattributes" "*.gitignore" "*.htm" "*.html" "*.ini" "*.js" \
	"*.json" "*.md" "*.prettierrc" "*.ps1" "*.psm1" "*.pubxml" \
	"*.py" "*.razor" "*.rest" "*.rs" "*.scss" "*.sh" "*.sln" \
	"*.slnx" "*.sql" "*.text" "*.toml" "*.ts" "*.txt" "*.vue" \
	"*.xml" "*.xsl" "*.yaml" "*.yml" "Makefile"
FIXEOL_LF_PATTERNS += $(FIXEOL_LF_PATTERNS_EXTRA)

require-format-eol:
	@case "$(EOL)" in lf|crlf) ;; *) echo "ERROR: EOL must be lf or crlf. Current value: $(EOL)"; exit 1 ;; esac

format: require-format-eol
	@set -e; \
	cd "$(REPO_ROOT)"; \
	workspaces=$$(find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type f \( -name '*.sln' -o -name '*.slnx' \) -print \)); \
	if test -n "$$workspaces"; then \
		$(DOTNET) --version >/dev/null 2>&1 || { echo "ERROR: $(DOTNET) is required because a .sln or .slnx file was found."; exit 1; }; \
		printf '%s\n' "$$workspaces" | while IFS= read -r workspace; do \
			test -n "$$workspace" || continue; \
			echo "Formatting .NET workspace: $$workspace"; \
			if find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type d -name Migrations -print -quit \) | grep -q .; then \
				find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type d -name Migrations -exec $(DOTNET) format "$$workspace" -v d --exclude {} + \); \
			else \
				$(DOTNET) format "$$workspace" -v d; \
			fi; \
		done; \
	fi; \
	if test -f package.json && grep -q '"format:web"[[:space:]]*:' package.json; then \
		$(NPM) --version >/dev/null 2>&1 || { echo "ERROR: $(NPM) is required because package.json declares scripts.format:web."; exit 1; }; \
		echo "Formatting web files with scripts.format:web..."; \
		$(NPM) run format:web --silent; \
	fi; \
	if test -f Cargo.toml; then \
		$(CARGO) --version >/dev/null 2>&1 || { echo "ERROR: $(CARGO) is required because Cargo.toml was found."; exit 1; }; \
		echo "Formatting Rust workspace: ./Cargo.toml"; \
		$(CARGO) fmt --manifest-path Cargo.toml --all; \
	else \
		cargo_manifests=$$(find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type f -name Cargo.toml -print \)); \
		if test -n "$$cargo_manifests"; then \
			$(CARGO) --version >/dev/null 2>&1 || { echo "ERROR: $(CARGO) is required because a Cargo.toml file was found."; exit 1; }; \
			printf '%s\n' "$$cargo_manifests" | while IFS= read -r manifest; do \
				test -n "$$manifest" || continue; \
				echo "Formatting Rust workspace: $$manifest"; \
				$(CARGO) fmt --manifest-path "$$manifest" --all; \
			done; \
		fi; \
	fi

	@command -v "$(FIXEOL)" >/dev/null 2>&1 || { echo "ERROR: $(FIXEOL) is required for EOL normalization."; exit 1; }
	@echo "Formatting files for $(EOL) line endings..."
	@cd "$(REPO_ROOT)" && $(FIXEOL) -encoding utf8 -eol $(EOL) -r $(FIXEOL_LF_PATTERNS) $(FIXEOL_EXCLUDE_ARGS)
	@cd "$(REPO_ROOT)" && $(FIXEOL) -encoding utf8 -eol crlf -r "*.cmd" $(FIXEOL_EXCLUDE_ARGS)
	@cd "$(REPO_ROOT)" && $(FIXEOL) -encoding utf8 -eol crlf -r "*.bat" $(FIXEOL_EXCLUDE_ARGS)

format-check:
	@set -e; \
	cd "$(REPO_ROOT)"; \
	workspaces=$$(find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type f \( -name '*.sln' -o -name '*.slnx' \) -print \)); \
	if test -n "$$workspaces"; then \
		$(DOTNET) --version >/dev/null 2>&1 || { echo "ERROR: $(DOTNET) is required because a .sln or .slnx file was found."; exit 1; }; \
		printf '%s\n' "$$workspaces" | while IFS= read -r workspace; do \
			test -n "$$workspace" || continue; \
			echo "Checking .NET workspace formatting: $$workspace"; \
			if find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type d -name Migrations -print -quit \) | grep -q .; then \
				find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type d -name Migrations -exec $(DOTNET) format "$$workspace" -v d --verify-no-changes --exclude {} + \); \
			else \
				$(DOTNET) format "$$workspace" -v d --verify-no-changes; \
			fi; \
		done; \
	fi; \
	if test -f package.json && grep -q '"format-check:web"[[:space:]]*:' package.json; then \
		$(NPM) --version >/dev/null 2>&1 || { echo "ERROR: $(NPM) is required because package.json declares scripts.format-check:web."; exit 1; }; \
		echo "Checking web files with scripts.format-check:web..."; \
		$(NPM) run format-check:web --silent; \
	fi; \
	if test -f Cargo.toml; then \
		$(CARGO) --version >/dev/null 2>&1 || { echo "ERROR: $(CARGO) is required because Cargo.toml was found."; exit 1; }; \
		echo "Checking Rust workspace formatting: ./Cargo.toml"; \
		$(CARGO) fmt --manifest-path Cargo.toml --all -- --check; \
	else \
		cargo_manifests=$$(find . \( -type d \( $(FORMAT_FIND_PRUNE) \) -prune \) -o \( -type f -name Cargo.toml -print \)); \
		if test -n "$$cargo_manifests"; then \
			$(CARGO) --version >/dev/null 2>&1 || { echo "ERROR: $(CARGO) is required because a Cargo.toml file was found."; exit 1; }; \
			printf '%s\n' "$$cargo_manifests" | while IFS= read -r manifest; do \
				test -n "$$manifest" || continue; \
				echo "Checking Rust workspace formatting: $$manifest"; \
				$(CARGO) fmt --manifest-path "$$manifest" --all -- --check; \
			done; \
		fi; \
	fi

endif
