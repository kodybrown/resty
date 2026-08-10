# Resty project Makefile.
# Keep common behavior in build-scripts/make/*.mk; adapt only this file.

.DEFAULT_GOAL := help

REPO_ROOT := $(subst \,/,$(dir $(abspath $(lastword $(MAKEFILE_LIST)))))
MAKE_MODULE_DIR := $(REPO_ROOT)build-scripts/make

PROJECT_NAME := Resty
SOLUTION := $(REPO_ROOT)Resty.sln
RUN_PROJECT := $(REPO_ROOT)Resty.Cli/Resty.Cli.csproj
PUBLISH_PROJECT := $(RUN_PROJECT)
REQUIRED_DOCS := README.md AGENTS.md LICENSE.md

DOTNET := dotnet
EOL := lf

CONFIGURATION := Debug
PUBLISH_CONFIGURATION := Release

ifeq ($(OS),Windows_NT)
	RID := win-x64
	EXE_SUFFIX := .exe
	USER_BIN ?= $(USERPROFILE)/Bin
	MAKE_HELPER := $(REPO_ROOT)build-scripts/make-helper.ps1
	RM_FILE = pwsh -NoProfile -File "$(MAKE_HELPER)" delete-file
	MAKE_DIR = pwsh -NoProfile -File "$(MAKE_HELPER)" make-dir
	CP_FILE = pwsh -NoProfile -File "$(MAKE_HELPER)" copy-file
	ZIP_DIR = pwsh -NoProfile -File "$(MAKE_HELPER)" zip-dir
	INSTALL_HELP = no-op on Windows
else
	UNAME_S := $(shell uname -s)
	UNAME_M := $(shell uname -m)
	EXE_SUFFIX :=
	USER_PREFIX ?= $(HOME)/.local
	USER_BIN ?= $(USER_PREFIX)/bin

	ifeq ($(UNAME_S),Darwin)
		ifeq ($(UNAME_M),arm64)
			RID := osx-arm64
		else
			RID := osx-x64
		endif
	else
		ifeq ($(UNAME_M),aarch64)
			RID := linux-arm64
		else ifeq ($(UNAME_M),arm64)
			RID := linux-arm64
		else
			RID := linux-x64
		endif
	endif

	RM_FILE = rm -f
	MAKE_DIR = mkdir -p
	CP_FILE = cp
	ZIP_DIR = ./build-scripts/make-helper.sh zip-dir
	INSTALL_HELP = Unix/macOS system install => $(INSTALL_PATH)
endif

READY_TO_RUN := true
PUBLISH_SELF_CONTAINED := true
PUBLISH_SINGLE_FILE := true
PUBLISH_DIR := $(REPO_ROOT)artifacts/publish/$(RID)
PUBLISH_ARGS := -p:PublishTrimmed=false
ARGS :=

PACKAGE_DIR := $(REPO_ROOT)artifacts/packages
PACKAGE_FILE := $(PACKAGE_DIR)/$(PROJECT_NAME)-$(RID).zip
PUBLISHED_BINARY := $(PUBLISH_DIR)/resty$(EXE_SUFFIX)
PREFIX ?= /usr/local
DESTDIR ?=
INSTALL_BINDIR := $(DESTDIR)$(PREFIX)/bin
INSTALL_PATH := $(INSTALL_BINDIR)/resty$(EXE_SUFFIX)
USER_INSTALL_PATH := $(USER_BIN)/resty$(EXE_SUFFIX)

include $(MAKE_MODULE_DIR)/repository.mk
include $(MAKE_MODULE_DIR)/format-files.mk
include $(MAKE_MODULE_DIR)/dotnet-build.mk
include $(MAKE_MODULE_DIR)/dotnet-run.mk
include $(MAKE_MODULE_DIR)/dotnet-publish.mk

.PHONY: help verify package install install-user uninstall

help:
	@echo "$(PROJECT_NAME) Makefile"
	@echo "  make check"
	@echo "  make restore"
	@echo "  make build CONFIGURATION=Debug"
	@echo "  make test CONFIGURATION=Debug"
	@echo "  make verify"
	@echo "  make run ARGS=--help"
	@echo "  make format"
	@echo "  make format-check"
	@echo "  make clean"
	@echo "  make publish RID=$(RID)"
	@echo "  make package RID=$(RID)"
	@echo "  make release RID=$(RID) READY_TO_RUN=true"
	@echo "  make install        $(INSTALL_HELP)"
	@echo "  make install-user   user install => $(USER_INSTALL_PATH)"
	@echo "  make uninstall"

verify: test
	$(MAKE) format-check
	$(MAKE) verify-docs

package: publish
	@$(MAKE_DIR) "$(PACKAGE_DIR)"
	@$(ZIP_DIR) "$(PUBLISH_DIR)" "$(PACKAGE_FILE)"
	@echo "Package ready: $(PACKAGE_FILE)"

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

include $(MAKE_MODULE_DIR)/dotnet-release.mk
