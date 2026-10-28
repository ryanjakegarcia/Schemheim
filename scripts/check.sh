#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${MANAGED_DIR:?Set MANAGED_DIR to the Valheim Managed directory.}"
: "${BEPINEX_DIR:?Set BEPINEX_DIR to the profile BepInEx directory.}"

dotnet test tests/Schemheim.Tests/Schemheim.Tests.csproj -c Release \
  -p:ManagedDir="$MANAGED_DIR" -p:BepInExDir="$BEPINEX_DIR"
