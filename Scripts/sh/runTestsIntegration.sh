#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
cd "$REPO_ROOT"

mkdir -p Scripts/logs

LOG=Scripts/logs/Content.IntegrationTests.log

if dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -m:1 -c DebugOpt "$@" -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed > "$LOG"; then
    STATUS=0
    printf '%s\n' "Integration tests passed. Log written to $LOG."
else
    STATUS=$?
    printf '%s\n' "Integration tests failed. Log written to $LOG."
    tail -n 80 "$LOG"
fi

printf '%s\n' "Tests complete. Press enter to continue."
IFS= read -r _ || true
exit "$STATUS"
