# Canonical .NET publish target.
# Configure PUBLISH_PROJECT and the PUBLISH_* variables before including.

ifndef CANONICAL_DOTNET_PUBLISH_MK_INCLUDED
CANONICAL_DOTNET_PUBLISH_MK_INCLUDED := 1

.PHONY: require-publish-project publish

require-publish-project:
	@test -f "$(PUBLISH_PROJECT)" || { echo "ERROR: $(PUBLISH_PROJECT) does not exist."; exit 1; }

publish: require-publish-project
	$(DOTNET) publish "$(PUBLISH_PROJECT)" \
		-c "$(PUBLISH_CONFIGURATION)" \
		-r "$(RID)" \
		--self-contained "$(PUBLISH_SELF_CONTAINED)" \
		-p:PublishSingleFile="$(PUBLISH_SINGLE_FILE)" \
		-p:PublishReadyToRun="$(READY_TO_RUN)" \
		-o "$(PUBLISH_DIR)" $(PUBLISH_ARGS)
	@echo "Published $(PROJECT_NAME) to $(PUBLISH_DIR)"

endif
