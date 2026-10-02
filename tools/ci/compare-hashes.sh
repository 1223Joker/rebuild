#!/usr/bin/env bash
# Cross-OS determinism gate: every runner's `rebuild-tools hashes` output must equal the golden file.
set -eu
golden=$(grep -v '^#' "$1" | sed '/^$/d')
shift
status=0
for f in "$@"; do
  if diff <(echo "$golden") <(tr -d '\r' < "$f" | sed '/^$/d') > /dev/null; then
    echo "OK   $f"
  else
    echo "DIFF $f"
    diff <(echo "$golden") <(tr -d '\r' < "$f" | sed '/^$/d') || true
    status=1
  fi
done
exit $status
