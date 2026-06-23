#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
cd "$REPO_ROOT"

mkdir -p Scripts/logs

dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj -m:1 -c DebugOpt "$@"
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c DebugOpt --no-build -- NUnit.ConsoleOut=0 > Scripts/logs/Content.YAMLLinter.log

printf '%s\n' "Tests complete. Press enter to continue."
IFS= read -r _ || true
