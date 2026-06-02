# =============================================================================
# Resty Makefile (Hybrid Windows/Linux)
#
# Targets:
#   restore             dotnet restore
#   build / build-cli   dotnet build
#   test                dotnet test
#
#   publish             dotnet publish to publish/<rid>/<configuration>/
#   package             publish + zip => builds/
#
#   install             Unix/macOS system install; no-op on Windows
#   install-user        publish + copy to user bin directory
#   uninstall           Unix/macOS system uninstall; no-op on Windows
#
#   clean               dotnet clean + remove publish/builds
#
# Configuration variables (override on the command line):
#   DOTNET_CFG          Debug or Release (default: Release)
#   RID                 runtime identifier; auto-detected by OS/architecture
#   SELF_CONTAINED      true/false (default: true)
#   PUBLISH_SINGLE      true/false (default: true)
#   PUBLISH_TRIMMED     true/false (default: false)
#   PREFIX              Unix install prefix (default: /usr/local)
#   DESTDIR             Unix package staging root (default: empty)
#
# Examples:
#   make build
#   make test
#   make publish
#   make package RID=linux-x64
#   make publish DOTNET_CFG=Debug SELF_CONTAINED=false
# =============================================================================

# -- Repo layout ---------------------------------------------------------------
REPO_ROOT    := $(dir $(abspath $(lastword $(MAKEFILE_LIST))))
SOLUTION     := $(REPO_ROOT)Resty.sln
CLI_PROJECT  := $(REPO_ROOT)Resty.Cli/Resty.Cli.csproj
TEST_PROJECT := $(REPO_ROOT)Tests/UnitTests/Resty.Tests.csproj

APP_NAME     ?= resty
BUILDS_DIR   := $(REPO_ROOT)builds
PUBLISH_ROOT := $(REPO_ROOT)publish
MAKE_HELPER  := $(REPO_ROOT)build-scripts/make-helper.ps1

# -- Configuration -------------------------------------------------------------
DOTNET_CFG      ?= Release
SELF_CONTAINED  ?= true
PUBLISH_SINGLE  ?= true
PUBLISH_TRIMMED ?= false
PREFIX          ?= /usr/local
DESTDIR         ?=
USER_PREFIX     ?= $(HOME)/.local

# -- OS Detection --------------------------------------------------------------
# $(OS) is automatically set to 'Windows_NT' by GNU Make on Windows.
ifeq ($(OS),Windows_NT)
	EXE_SUFFIX := .exe
	DEFAULT_RID := $(shell pwsh -NoProfile -Command "if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }")
	DOTNET_REQUIRED_MAJOR := $(shell pwsh -NoProfile -Command "(Select-String -Pattern 'net(\d+)' '$(CLI_PROJECT)').Matches.Groups[1].Value")
	TIMESTAMP := $(shell pwsh -NoProfile -Command "Get-Date -Format 'yyyyMMdd'")

	RM_DIR = pwsh -NoProfile -File "$(MAKE_HELPER)" delete-dir
	RM_FILE = pwsh -NoProfile -File "$(MAKE_HELPER)" delete-file
	MAKE_DIR = pwsh -NoProfile -File "$(MAKE_HELPER)" make-dir
	CP_FILE = pwsh -NoProfile -File "$(MAKE_HELPER)" copy-file
	ZIP_DIR = pwsh -NoProfile -File "$(MAKE_HELPER)" zip-dir

	USER_BIN ?= $(USERPROFILE)/Bin
	INSTALL_HELP = no-op on Windows
else
	UNAME_S := $(shell uname -s)
	UNAME_M := $(shell uname -m)
	EXE_SUFFIX :=

	ifeq ($(UNAME_S),Darwin)
		ifeq ($(UNAME_M),arm64)
			DEFAULT_RID := osx-arm64
		else
			DEFAULT_RID := osx-x64
		endif
	else
		ifeq ($(UNAME_M),aarch64)
			DEFAULT_RID := linux-arm64
		else ifeq ($(UNAME_M),arm64)
			DEFAULT_RID := linux-arm64
		else
			DEFAULT_RID := linux-x64
		endif
	endif

	DOTNET_REQUIRED_MAJOR := $(shell sed -n 's/.*<TargetFramework>net\([0-9]*\).*/\1/p' "$(CLI_PROJECT)" | head -n 1)
	TIMESTAMP := $(shell date +%Y%m%d)

	RM_DIR = rm -rf
	RM_FILE = rm -f
	MAKE_DIR = mkdir -p
	CP_FILE = cp
	ZIP_DIR = sh -c 'cd "$$1" && zip -qr "$$2" .' --

	USER_BIN ?= $(USER_PREFIX)/bin
	INSTALL_HELP = Unix/macOS system install => $(INSTALL_PATH)
endif

RID ?= $(DEFAULT_RID)

# -- Derived paths -------------------------------------------------------------
PUBLISH_DIR := $(PUBLISH_ROOT)/$(RID)/$(DOTNET_CFG)
PUBLISHED_BINARY := $(PUBLISH_DIR)/$(APP_NAME)$(EXE_SUFFIX)
PACKAGE_FILE := $(BUILDS_DIR)/$(TIMESTAMP)-$(APP_NAME)-$(RID).zip
INSTALL_BINDIR := $(DESTDIR)$(PREFIX)/bin
INSTALL_PATH := $(INSTALL_BINDIR)/$(APP_NAME)$(EXE_SUFFIX)
USER_INSTALL_PATH := $(USER_BIN)/$(APP_NAME)$(EXE_SUFFIX)

# =============================================================================
.PHONY: \
	help check restore \
	build build-cli test \
	publish package \
	install install-user uninstall \
	clean clean-publish clean-builds

.DEFAULT_GOAL := help

# -- Help ----------------------------------------------------------------------
help:
	@echo "Resty Makefile (Hybrid)"
	@echo ""
	@echo "Configuration (current values):"
	@echo "  DOTNET_CFG       = $(DOTNET_CFG)"
	@echo "  RID              = $(RID)"
	@echo "  SELF_CONTAINED   = $(SELF_CONTAINED)"
	@echo "  PUBLISH_SINGLE   = $(PUBLISH_SINGLE)"
	@echo "  PUBLISH_TRIMMED  = $(PUBLISH_TRIMMED)"
	@echo "  PREFIX           = $(PREFIX)"
	@echo "  DESTDIR          = $(DESTDIR)"
	@echo "  USER_BIN         = $(USER_BIN)"
	@echo ""
	@echo "Build targets:"
	@echo "  make restore"
	@echo "  make build       (or build-cli)"
	@echo "  make test"
	@echo ""
	@echo "Publish targets:"
	@echo "  make publish     output => $(PUBLISH_DIR)"
	@echo "  make package     output => $(PACKAGE_FILE)"
	@echo ""
	@echo "Install targets:"
	@echo "  make install        $(INSTALL_HELP)"
	@echo "  make install-user   user install => $(USER_INSTALL_PATH)"
	@echo "  make uninstall"
	@echo ""
	@echo "Clean targets:"
	@echo "  make clean"
	@echo ""
	@echo "Other:"
	@echo "  make check"

# -- Check ---------------------------------------------------------------------
check:
	@echo "Checking prerequisites..."
	@dotnet --version >/dev/null 2>&1 || { echo "  FAIL  dotnet is not installed"; exit 1; }
	@DOTNET_VER=$$(dotnet --version); \
		DOTNET_MAJOR=$$(echo "$$DOTNET_VER" | cut -d. -f1); \
		if [ "$$DOTNET_MAJOR" = "$(DOTNET_REQUIRED_MAJOR)" ]; then \
			echo "  OK    dotnet $$DOTNET_VER  (requires SDK $(DOTNET_REQUIRED_MAJOR).x)"; \
		else \
			echo "  FAIL  dotnet $$DOTNET_VER  (requires SDK $(DOTNET_REQUIRED_MAJOR).x)"; exit 1; \
		fi
	@echo "All prerequisites satisfied."

# -- Build ---------------------------------------------------------------------
restore:
	dotnet restore "$(SOLUTION)"

build: build-cli

build-cli:
	dotnet build "$(CLI_PROJECT)" -c $(DOTNET_CFG)

test:
	dotnet test "$(TEST_PROJECT)" -c $(DOTNET_CFG)

# -- Publish -------------------------------------------------------------------
publish:
	@echo "[publish] DOTNET_CFG=$(DOTNET_CFG) RID=$(RID)"
	dotnet publish "$(CLI_PROJECT)" \
		-c $(DOTNET_CFG) \
		-r $(RID) \
		--self-contained $(SELF_CONTAINED) \
		-p:PublishSingleFile=$(PUBLISH_SINGLE) \
		-p:PublishTrimmed=$(PUBLISH_TRIMMED) \
		-o "$(PUBLISH_DIR)"
	@echo "[publish] => $(PUBLISH_DIR)"

package: publish
	@$(MAKE_DIR) "$(BUILDS_DIR)"
	@$(ZIP_DIR) "$(PUBLISH_DIR)" "$(PACKAGE_FILE)"
	@echo "[package] => $(PACKAGE_FILE)"

# -- Install -------------------------------------------------------------------
ifeq ($(OS),Windows_NT)

install:
	@echo "[install] no-op on Windows; use 'make install-user' to copy to $(USER_INSTALL_PATH)"

install-user: publish
	@$(MAKE_DIR) "$(USER_BIN)"
	@$(CP_FILE) "$(PUBLISHED_BINARY)" "$(USER_INSTALL_PATH)"
	@echo "[install-user] => $(USER_INSTALL_PATH)"

uninstall:
	@echo "[uninstall] no-op on Windows"

else

install: publish
	@$(MAKE_DIR) "$(INSTALL_BINDIR)"
	@$(CP_FILE) "$(PUBLISHED_BINARY)" "$(INSTALL_PATH)"
	@chmod 0755 "$(INSTALL_PATH)"
	@echo "[install] => $(INSTALL_PATH)"

install-user:
	@$(MAKE) install PREFIX="$(USER_PREFIX)"

uninstall:
	@$(RM_FILE) "$(INSTALL_PATH)"
	@echo "[uninstall] removed $(INSTALL_PATH)"

endif

# -- Clean ---------------------------------------------------------------------
clean: clean-publish clean-builds
	dotnet clean "$(SOLUTION)" -c $(DOTNET_CFG)

clean-publish:
	@$(RM_DIR) "$(PUBLISH_ROOT)"

clean-builds:
	@$(RM_DIR) "$(BUILDS_DIR)"
