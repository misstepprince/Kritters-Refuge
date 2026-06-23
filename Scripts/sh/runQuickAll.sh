#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

sh -e "$SCRIPT_DIR/runQuickServer.sh" "$@" &
sh -e "$SCRIPT_DIR/runQuickClient.sh" "$@"
