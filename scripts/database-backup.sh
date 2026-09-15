#!/usr/bin/env bash
# Daily database backup (runs from cron).
#
# Until 2026-09-15 WheyProof had no backup at all: the only nightly dump on
# this VM was the Turkish sister site's. The price history collected here
# can't be recreated, so losing the volume would lose the site's core data.
#
# The backup is TAKEN AND VERIFIED; an unverified dump isn't a backup.
#
# VERIFICATION HAS TWO STEPS:
#   1. `pg_restore -l`: the archive's table of contents is readable. Cheap but
#      WEAK: it reads the header only, never the data. A corrupt dump can pass
#      this and still fail on restore.
#   2. A REAL RESTORE into a separate database, then a row count check against
#      the live database.
#
# SAFETY: the restore target is a FIXED name that differs from the live
# database. It isn't taken from a variable, so a wrong value can't overwrite
# the live schema.
set -euo pipefail

PROJECT=/home/ubuntu/wheyproof
TARGET=/home/ubuntu/wheyproof-backups
RETENTION_DAYS=14
DB_USER=wheyproof
# Live database: wheyproof. The name below MUST differ from it.
CHECK_DB=wheyproof_backup_check

mkdir -p "$TARGET"
cd "$PROJECT"

STAMP=$(date +%Y%m%d-%H%M)
CONTAINER_PATH=/tmp/backup-$STAMP.dump
HOST_PATH="$TARGET/wheyproof-$STAMP.dump"

run_psql() {
  docker compose exec -T db psql -U "$DB_USER" -d postgres -tAc "$1" </dev/null
}

drop_check_db() {
  run_psql "DROP DATABASE IF EXISTS $CHECK_DB;" > /dev/null 2>&1 || true
}

# However the script ends (errors included), don't leave the temporary
# database behind.
trap drop_check_db EXIT

# The dump is taken inside the container over the local socket: no password
# is needed, so no secret lands on a command line or in a log.
docker compose exec -T db pg_dump -U "$DB_USER" -d wheyproof \
  -Fc --no-owner --no-privileges -f "$CONTAINER_PATH" </dev/null

# --- step 1: can the archive contents be listed?
docker compose exec -T db pg_restore -l "$CONTAINER_PATH" </dev/null > /dev/null

# --- step 2: does the dump REALLY restore?
drop_check_db
run_psql "CREATE DATABASE $CHECK_DB;" > /dev/null

docker compose exec -T db pg_restore -U "$DB_USER" -d "$CHECK_DB" \
  --no-owner --no-privileges "$CONTAINER_PATH" </dev/null > /dev/null

# Row counts: the dump was just taken, so it should match the live database
# almost exactly. Exact equality ISN'T required: a scrape can write between
# the dump and this check. A 5% tolerance covers that window and still
# catches real loss (a half-restored or empty dump).
LIVE=$(docker compose exec -T db psql -U "$DB_USER" -d wheyproof -tAc \
  'SELECT COUNT(*) FROM "Products";' </dev/null | tr -d '[:space:]')
RESTORED=$(docker compose exec -T db psql -U "$DB_USER" -d "$CHECK_DB" -tAc \
  'SELECT COUNT(*) FROM "Products";' </dev/null | tr -d '[:space:]')

if [ "${RESTORED:-0}" -lt 1 ]; then
  echo "ERROR: restored backup has no products (live: $LIVE)" >&2
  exit 1
fi

DIFF=$(( LIVE > RESTORED ? LIVE - RESTORED : RESTORED - LIVE ))
if [ "$LIVE" -gt 0 ] && [ $(( DIFF * 100 / LIVE )) -gt 5 ]; then
  echo "ERROR: restored row count doesn't match (live: $LIVE, restored: $RESTORED)" >&2
  exit 1
fi

drop_check_db

docker compose cp "db:$CONTAINER_PATH" "$HOST_PATH" </dev/null
docker compose exec -T db rm -f "$CONTAINER_PATH" </dev/null

# Size is a health signal too: a backup that suddenly shrinks points to
# silent data loss.
SIZE=$(stat -c %s "$HOST_PATH")
if [ "$SIZE" -lt 200000 ]; then
  echo "WARNING: backup smaller than expected ($SIZE bytes): $HOST_PATH" >&2
  exit 1
fi

find "$TARGET" -name 'wheyproof-*.dump' -mtime +$RETENTION_DAYS -delete
echo "$(date '+%Y-%m-%d %H:%M') backup ok: $HOST_PATH ($SIZE bytes, restore verified: $RESTORED products)"
