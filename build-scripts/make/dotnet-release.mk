# Canonical .NET release orchestration.
# Include the build and publish modules, and define verify, before including.

ifndef CANONICAL_DOTNET_RELEASE_MK_INCLUDED
CANONICAL_DOTNET_RELEASE_MK_INCLUDED := 1

.PHONY: release

release: verify
	$(MAKE) publish
	@echo "Release directory ready: $(PUBLISH_DIR)"

endif
