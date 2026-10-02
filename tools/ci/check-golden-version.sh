#!/usr/bin/env bash
# Golden-value gate (docs/08-testing.md §3): an existing golden value in tests/golden/hashes.txt may only
# change together with a version bump in the same file.
#   probe / replay lines changed  -> the `version` line (GameVersion) must change too
#   map lines changed             -> the `generator` line (MapGenerator.Version) or `version` must change
# New or removed entries need no bump. Usage: check-golden-version.sh <base-commit>
set -eu
export LC_ALL=C
cd "$(dirname "$0")/../.."
base=${1:-}
file=tests/golden/hashes.txt
if [ -z "$base" ] || [ "$base" = "0000000000000000000000000000000000000000" ] || ! git cat-file -e "$base^{commit}" 2>/dev/null; then
  echo "OK: no base commit to compare against"
  exit 0
fi
if ! git cat-file -e "$base:$file" 2>/dev/null; then
  echo "OK: $file does not exist in $base"
  exit 0
fi

# key<TAB>value per line; keys of replay/map lines include the entry name.
keyed() {
  grep -v '^#' | sed '/^$/d' | tr -d '\r' | awk '{
    if ($1 == "replay" || $1 == "map") { k = $1 " " $2; $1 = ""; $2 = "" } else { k = $1; $1 = "" }
    sub(/^ +/, ""); print k "\t" $0 }' | sort
}
old=$(git show "$base:$file" | keyed)
new=$(keyed < "$file")
changed=$(join -t "$(printf '\t')" <(echo "$old") <(echo "$new") | awk -F '\t' '$2 != $3 { print $1 }')

has() { grep -qx "$1" <<<"$changed"; }
status=0
while IFS= read -r key; do
  [ -z "$key" ] && continue
  case "$key" in
    version|generator) ;;
    map\ *) if ! has generator && ! has version; then echo "FAIL: '$key' changed without a generator/version bump"; status=1; fi ;;
    *) if ! has version; then echo "FAIL: '$key' changed without a GameVersion bump"; status=1; fi ;;
  esac
done <<<"$changed"
[ $status -eq 0 ] && echo "OK: golden changes since ${base:0:12} are covered by version bumps"
exit $status
