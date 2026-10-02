#!/usr/bin/env bash
# Proves the determinism guard works end to end: adding a float to Rebuild.Sim must fail the build.
set -u
cd "$(dirname "$0")/../.."
probe=src/Rebuild.Sim/__FloatBanProbe.cs
trap 'rm -f "$probe"' EXIT
printf 'namespace Rebuild.Sim;\ninternal static class FloatBanProbe { internal static float Value = 1.5f; }\n' > "$probe"
if output=$(dotnet build src/Rebuild.Sim -c Debug --nologo 2>&1); then
  echo "FAIL: Rebuild.Sim built although it contains a float"
  exit 1
fi
if ! grep -q "RB0001" <<<"$output"; then
  echo "FAIL: build failed, but not with RB0001:"
  echo "$output" | tail -20
  exit 1
fi
echo "OK: float in Rebuild.Sim is rejected with RB0001"
