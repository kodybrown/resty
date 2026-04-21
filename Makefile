# Resty - build, publish, and install targets for local Linux usage.

APP_NAME          ?= resty
PROJECT           ?= Resty.Cli/Resty.Cli.csproj
CONFIGURATION     ?= Release
PREFIX            ?= /usr/local
DESTDIR           ?=
SELF_CONTAINED    ?= true
PUBLISH_SINGLE    ?= true
PUBLISH_TRIMMED   ?= false

UNAME_S := $(shell uname -s)
UNAME_M := $(shell uname -m)

ifeq ($(UNAME_S),Linux)
ifeq ($(UNAME_M),x86_64)
RID ?= linux-x64
else ifeq ($(UNAME_M),amd64)
RID ?= linux-x64
else ifeq ($(UNAME_M),aarch64)
RID ?= linux-arm64
else ifeq ($(UNAME_M),arm64)
RID ?= linux-arm64
else
RID ?= linux-x64
endif
else
RID ?= linux-x64
endif

PUBLISH_DIR       ?= publish/$(RID)/$(CONFIGURATION)
PUBLISHED_BINARY  := $(PUBLISH_DIR)/$(APP_NAME)
INSTALL_BINDIR    := $(DESTDIR)$(PREFIX)/bin
INSTALL_PATH      := $(INSTALL_BINDIR)/$(APP_NAME)
USER_PREFIX       ?= $(HOME)/.local

.PHONY: all help build publish install install-user uninstall clean

all: help

help:
	@echo "Resty make targets:"
	@echo "  build         Run dotnet build"
	@echo "  publish       Produce a self-contained single-file binary"
	@echo "  install       Publish and install to \$$PREFIX/bin (default /usr/local/bin)"
	@echo "  install-user  Publish and install to \$$HOME/.local/bin"
	@echo "  uninstall     Remove the installed binary from \$$PREFIX/bin"
	@echo "  clean         Remove publish output"
	@echo ""
	@echo "Variables:"
	@echo "  RID=$(RID)"
	@echo "  CONFIGURATION=$(CONFIGURATION)"
	@echo "  PREFIX=$(PREFIX)"
	@echo "  DESTDIR=$(DESTDIR)"

build:
	dotnet build $(PROJECT) -c $(CONFIGURATION)

publish:
	dotnet publish $(PROJECT) \
		-c $(CONFIGURATION) \
		-r $(RID) \
		--self-contained $(SELF_CONTAINED) \
		/p:PublishSingleFile=$(PUBLISH_SINGLE) \
		/p:PublishTrimmed=$(PUBLISH_TRIMMED) \
		-o $(PUBLISH_DIR)

install: publish
	install -d $(INSTALL_BINDIR)
	install -m 0755 $(PUBLISHED_BINARY) $(INSTALL_PATH)
	@echo "Installed $(APP_NAME) to $(INSTALL_PATH)"

install-user:
	@$(MAKE) install PREFIX=$(USER_PREFIX)

uninstall:
	rm -f $(INSTALL_PATH)
	@echo "Removed $(INSTALL_PATH)"

clean:
	rm -rf publish
