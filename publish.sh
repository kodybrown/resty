#!/bin/bash
#
# Resty Build and Publish Script (Bash)
#
# This script builds Resty as a single-file, self-contained binary
# and optionally copies it to the user's bin directory.
#

set -euo pipefail

# Default values
CONFIGURATION="Release"
RUNTIME=""
OUTPUT_PATH="./publish"
SKIP_COPY=false
VERBOSE=false
HELP=false

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

show_help() {
    echo -e "${GREEN}Resty Build and Publish Script${NC}"
    echo ""
    echo "USAGE:"
    echo "  ./publish.sh [OPTIONS]"
    echo ""
    echo "OPTIONS:"
    echo "  -c, --configuration <config>  Build configuration (Debug/Release, default: Release)"
    echo "  -r, --runtime <rid>          Target runtime identifier (auto-detected if not specified)"
    echo "  -o, --output <path>          Output directory (default: ./publish)"
    echo "  -s, --skip-copy              Skip copying to ~/bin directory"
    echo "  -v, --verbose                Show detailed build output"
    echo "  -h, --help                   Show this help message"
    echo ""
    echo "EXAMPLES:"
    echo "  ./publish.sh                           # Build for current platform"
    echo "  ./publish.sh -r win-x64                # Build for Windows x64"
    echo "  ./publish.sh -r linux-x64              # Build for Linux x64"
    echo "  ./publish.sh -r osx-x64                # Build for macOS x64"
    echo "  ./publish.sh --skip-copy               # Don't copy to ~/bin"
    echo "  ./publish.sh -c Debug                  # Debug build"
    echo ""
    echo "SUPPORTED RUNTIMES:"
    echo "  win-x64, win-x86, win-arm64"
    echo "  linux-x64, linux-arm64"
    echo "  osx-x64, osx-arm64"
}

get_default_runtime() {
    local os_name=$(uname -s)
    local arch=$(uname -m)

    case "$os_name" in
        Linux*)
            case "$arch" in
                x86_64|amd64)
                    echo "linux-x64"
                    ;;
                aarch64|arm64)
                    echo "linux-arm64"
                    ;;
                *)
                    echo "linux-x64"  # Fallback
                    ;;
            esac
            ;;
        Darwin*)
            case "$arch" in
                x86_64|amd64)
                    echo "osx-x64"
                    ;;
                arm64)
                    echo "osx-arm64"
                    ;;
                *)
                    echo "osx-x64"  # Fallback
                    ;;
            esac
            ;;
        CYGWIN*|MINGW*|MSYS*)
            case "$arch" in
                x86_64|amd64)
                    echo "win-x64"
                    ;;
                *)
                    echo "win-x64"  # Fallback
                    ;;
            esac
            ;;
        *)
            echo "linux-x64"  # Fallback
            ;;
    esac
}

get_executable_name() {
    local runtime="$1"

    if [[ "$runtime" == win* ]]; then
        echo "resty.exe"
    else
        echo "resty"
    fi
}

get_bin_directory() {
    if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" ]]; then
        echo "$USERPROFILE/Bin"
    else
        echo "$HOME/bin"
    fi
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        -r|--runtime)
            RUNTIME="$2"
            shift 2
            ;;
        -o|--output)
            OUTPUT_PATH="$2"
            shift 2
            ;;
        -s|--skip-copy)
            SKIP_COPY=true
            shift
            ;;
        -v|--verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            HELP=true
            shift
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}" >&2
            echo "Use --help for usage information." >&2
            exit 1
            ;;
    esac
done

# Show help if requested
if [[ "$HELP" == "true" ]]; then
    show_help
    exit 0
fi

# Auto-detect runtime if not specified
if [[ -z "$RUNTIME" ]]; then
    RUNTIME=$(get_default_runtime)
    echo -e "${CYAN}Auto-detected runtime: $RUNTIME${NC}"
fi

# Get executable name based on runtime
EXECUTABLE_NAME=$(get_executable_name "$RUNTIME")

# Set script location as working directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo -e "${YELLOW}Building Resty ($CONFIGURATION, $RUNTIME)...${NC}"
echo ""

# Build command arguments
BUILD_ARGS=(
    "publish"
    "Resty.Cli/Resty.Cli.csproj"
    "-c" "$CONFIGURATION"
    "-r" "$RUNTIME"
    "--self-contained" "true"
    "/p:PublishSingleFile=true"
    "/p:PublishTrimmed=true"
    "-o" "$OUTPUT_PATH"
)

if [[ "$VERBOSE" == "true" ]]; then
    BUILD_ARGS+=("--verbosity" "normal")
else
    BUILD_ARGS+=("--verbosity" "minimal")
fi

# Execute build
if dotnet "${BUILD_ARGS[@]}"; then
    echo ""
    echo -e "${GREEN}✅ Publish succeeded!${NC}"

    OUTPUT_FILE="$OUTPUT_PATH/$EXECUTABLE_NAME"
    if [[ -f "$OUTPUT_FILE" ]]; then
        FILE_SIZE_BYTES=$(stat -f%z "$OUTPUT_FILE" 2>/dev/null || stat -c%s "$OUTPUT_FILE" 2>/dev/null || echo "0")
        FILE_SIZE_MB=$(echo "scale=2; $FILE_SIZE_BYTES / 1048576" | bc 2>/dev/null || echo "$(($FILE_SIZE_BYTES / 1048576))")
        echo -e "${GRAY}   📦 Output: $OUTPUT_FILE (${FILE_SIZE_MB} MB)${NC}"
    fi

    # Copy to bin directory if not skipped
    if [[ "$SKIP_COPY" != "true" ]]; then
        BIN_DIR=$(get_bin_directory)
        BIN_FILE="$BIN_DIR/$EXECUTABLE_NAME"

        if [[ -d "$BIN_DIR" ]]; then
            echo ""
            echo -e "${CYAN}📋 Copying to $BIN_FILE...${NC}"
            if cp "$OUTPUT_FILE" "$BIN_FILE"; then
                # Make executable
                chmod +x "$BIN_FILE"
                echo -e "${GREEN}✅ Copy succeeded!${NC}"
            else
                echo -e "${YELLOW}⚠️  Copy failed${NC}"
                echo -e "${GRAY}   You can manually copy: $OUTPUT_FILE -> $BIN_FILE${NC}"
            fi
        else
            echo ""
            echo -e "${YELLOW}⚠️  Bin directory not found: $BIN_DIR${NC}"
            echo -e "${GRAY}   Create the directory and add it to your PATH, then copy manually:${NC}"
            echo -e "${GRAY}   $OUTPUT_FILE -> $BIN_FILE${NC}"
        fi
    fi

    echo ""
    echo -e "${GREEN}🚀 Build completed successfully!${NC}"
    exit 0
else
    echo ""
    echo -e "${RED}❌ Publish failed!${NC}"
    echo -e "${GRAY}   Check the build output above for errors.${NC}"

    if [[ "$VERBOSE" != "true" ]]; then
        echo -e "${GRAY}   Try running with --verbose for more details.${NC}"
    fi

    echo -n "Press Enter to exit..."
    read -r
    exit 1
fi
