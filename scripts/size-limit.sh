#!/usr/bin/env bash
# Source file size limit (security review B5, 26 Sept).
#
# DealsQueryService and the admin endpoints were split, but nothing stopped
# them from growing back (on the Turkish site deals-list.ts went from 1102 to
# 1107 lines right after the split). This script runs in the deploy gate:
#   - A new or unlisted file may not exceed LIMIT lines.
#   - Files over the limit today are FROZEN at today's size (CEILING): they
#     can't grow. If one must, split it first, or raise its ceiling here on
#     purpose so it shows in the commit.
#   - When a frozen file shrinks the script says so: lower the ceiling too, so
#     the space won isn't spent again.
# CSS is out of scope: component styles already have Angular's byte budget.
# Migrations are EF-generated, and tests (.spec.ts) aren't counted.
set -euo pipefail
cd "$(dirname "$0")/.."

LIMIT=800
declare -A CEILING=(
  [frontend/src/app/admin-page/admin-page.html]=1505
  [frontend/src/app/admin-page/admin-page.ts]=977
  [frontend/src/app/deals-list/deals-list.ts]=989
  [backend/src/IndirimTakip.Infrastructure/Deals/DealsQueryService.cs]=814
)

# The list is taken into a variable FIRST: set -e doesn't see the exit code of
# a process substitution (< <(...)); when git failed, the loop read zero files
# and printed "OK" (measured, 27 Sept). Here a git failure stops the script at
# the assignment; if the list comes back empty for another reason, the counter
# below does.
list=$(git ls-files 'backend/src/*.cs' 'frontend/src/*.ts' 'frontend/src/*.html')
failed=0
count=0
while IFS= read -r file; do
  [[ -z $file || $file == */Migrations/* || $file == *.spec.ts ]] && continue
  count=$((count + 1))
  lines=$(wc -l < "$file")
  allowed=${CEILING[$file]:-$LIMIT}
  if (( lines > allowed )); then
    echo "LIMIT EXCEEDED: $file has $lines lines (allowed $allowed)"
    failed=1
  fi
done <<< "$list"

if (( count == 0 )); then
  echo "ERROR: no files were read — is the script running inside the git repository?"
  exit 1
fi

for file in "${!CEILING[@]}"; do
  if [[ ! -f $file ]]; then
    echo "NOTE: $file no longer exists, remove it from CEILING."
  elif (( $(wc -l < "$file") < CEILING[$file] )); then
    echo "NOTE: $file is down to $(wc -l < "$file") lines; lower its ceiling too (now ${CEILING[$file]})."
  fi
done

if (( failed )); then
  echo "Split the file, or (on purpose) raise its ceiling in scripts/size-limit.sh."
  exit 1
fi
echo "Size limit OK ($count files; limit $LIMIT lines, ${#CEILING[@]} files frozen at today's size)."
