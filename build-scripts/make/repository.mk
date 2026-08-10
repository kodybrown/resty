# Canonical repository checks shared across projects.
# Configure REPO_ROOT, SOLUTION, DOTNET, and REQUIRED_DOCS before including.

ifndef CANONICAL_REPOSITORY_MK_INCLUDED
CANONICAL_REPOSITORY_MK_INCLUDED := 1

.PHONY: check verify-docs

check:
	@command -v "$(DOTNET)" >/dev/null 2>&1 || { echo "FAIL: $(DOTNET) is not installed."; exit 1; }
	@echo "OK: dotnet $$($(DOTNET) --version)"
	@command -v git >/dev/null 2>&1 || { echo "FAIL: git is not installed."; exit 1; }
	@echo "OK: git $$(git --version)"
	@test -f "$(SOLUTION)" || { echo "FAIL: $(SOLUTION) is missing."; exit 1; }
	@echo "OK: $(SOLUTION)"

verify-docs:
	@set -e; for file in $(REQUIRED_DOCS); do \
		test -f "$(REPO_ROOT)$$file" || { echo "FAIL: required document is missing: $$file"; exit 1; }; \
	done
	@git diff --check
	@echo "OK: required documentation exists and tracked diffs have no whitespace errors."

endif
