# Canonical .NET run target.
# Configure RUN_PROJECT, DOTNET, CONFIGURATION, and optional ARGS before including.

ifndef CANONICAL_DOTNET_RUN_MK_INCLUDED
CANONICAL_DOTNET_RUN_MK_INCLUDED := 1

.PHONY: require-run-project run

require-run-project:
	@test -f "$(RUN_PROJECT)" || { echo "ERROR: $(RUN_PROJECT) does not exist."; exit 1; }

run: require-run-project
	$(DOTNET) run --project "$(RUN_PROJECT)" -c "$(CONFIGURATION)" -- $(ARGS)

endif
