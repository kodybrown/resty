#!/usr/bin/env bash
set -euo pipefail

# chmod +x ./build-scripts/make-helper.sh

cmd="${1:-}"

case "$cmd" in
  zip-dir)
    if [[ $# -ne 3 ]]; then
      echo "Usage: make-helper.sh zip-dir [source_dir] [destination.zip]" >&2
      exit 1
    fi

    source_dir="${2%/}"
    zip_file="${3%/}"

    if [[ ! -d "$source_dir" ]]; then
      echo "Source directory does not exist: $source_dir" >&2
      exit 1
    fi

    parent_dir="$(dirname "$source_dir")"
    base_dir="$(basename "$source_dir")"

    mkdir -p "$(dirname "$zip_file")"

    (
      cd "$parent_dir"
      zip -r "$zip_file" "$base_dir"
    )
    ;;

  *)
    echo "Unknown command: $cmd" >&2
    exit 1
    ;;
esac
