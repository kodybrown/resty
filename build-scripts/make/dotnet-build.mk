# Canonical .NET restore, build, test, and clean targets.
# Configure SOLUTION, DOTNET, and CONFIGURATION before including.

ifndef CANONICAL_DOTNET_BUILD_MK_INCLUDED
CANONICAL_DOTNET_BUILD_MK_INCLUDED := 1

.PHONY: require-solution restore build test clean

require-solution:
	@test -f "$(SOLUTION)" || { echo "ERROR: $(SOLUTION) does not exist."; exit 1; }

restore: require-solution
	$(DOTNET) restore "$(SOLUTION)"

build: restore
	$(DOTNET) build "$(SOLUTION)" -c "$(CONFIGURATION)" --no-restore

test: build
	$(DOTNET) test "$(SOLUTION)" -c "$(CONFIGURATION)" --no-build

clean: require-solution
	$(DOTNET) clean "$(SOLUTION)" -c "$(CONFIGURATION)"

endif
