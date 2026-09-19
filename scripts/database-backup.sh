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

run_psql() {
  docker compose exec -T db psql -U "$DB_USER" -d postgres -tAc "$1" </dev/null
}

drop_check_db() {
  run_psql "DROP DATABASE IF EXISTS $CHECK_DB;" > /dev/null 2>&1 || true
}

# However the script ends (errors included), don't leave the temporary
# database behind.
trap drop_check_db EXIT

# One database per edition: "wheyproof" is the US site, "wheyproof_uk" the UK
# section (the same app run a second time, see docker-compose.yml). Each gets
# its own dump file and its own restore check.
#   $1 database   $2 file prefix   $3 minimum dump size in bytes (0 = no floor)
backup_database() {
  local db=$1 prefix=$2 min_size=$3
  local container_path=/tmp/backup-$prefix-$STAMP.dump
  local host_path="$TARGET/$prefix-$STAMP.dump"

  # The dump is taken inside the container over the local socket: no password
  # is needed, so no secret lands on a command line or in a log.
  docker compose exec -T db pg_dump -U "$DB_USER" -d "$db" \
    -Fc --no-owner --no-privileges -f "$container_path" </dev/null

  # --- step 1: can the archive contents be listed?
  docker compose exec -T db pg_restore -l "$container_path" </dev/null > /dev/null

  # --- step 2: does the dump REALLY restore?
  drop_check_db
  run_psql "CREATE DATABASE $CHECK_DB;" > /dev/null
  docker compose exec -T db pg_restore -U "$DB_USER" -d "$CHECK_DB" \
    --no-owner --no-privileges "$container_path" </dev/null > /dev/null

  # Row counts: the dump was just taken, so it should match the live database
  # almost exactly. Exact equality ISN'T required: a scrape can write between
  # the dump and this check. A 5% tolerance covers that window and still
  # catches real loss (a half-restored or empty dump).
  local live restored
  live=$(docker compose exec -T db psql -U "$DB_USER" -d "$db" -tAc \
    'SELECT COUNT(*) FROM "Products";' </dev/null | tr -d '[:space:]')
  restored=$(docker compose exec -T db psql -U "$DB_USER" -d "$CHECK_DB" -tAc \
    'SELECT COUNT(*) FROM "Products";' </dev/null | tr -d '[:space:]')

  # An empty restore is an error only when the live database isn't empty: the
  # UK section starts with no products, and that is not a broken backup.
  if [ "${live:-0}" -gt 0 ] && [ "${restored:-0}" -lt 1 ]; then
    echo "ERROR: $db: restored backup has no products (live: $live)" >&2
    exit 1
  fi
  local diff=$(( live > restored ? live - restored : restored - live ))
  if [ "$live" -gt 0 ] && [ $(( diff * 100 / live )) -gt 5 ]; then
    echo "ERROR: $db: restored row count doesn't match (live: $live, restored: $restored)" >&2
    exit 1
  fi

  drop_check_db

  docker compose cp "db:$container_path" "$host_path" </dev/null
  docker compose exec -T db rm -f "$container_path" </dev/null

  # Size is a health signal too: a backup that suddenly shrinks points to
  # silent data loss.
  local size
  size=$(stat -c %s "$host_path")
  if [ "$min_size" -gt 0 ] && [ "$size" -lt "$min_size" ]; then
    echo "WARNING: $db: backup smaller than expected ($size bytes): $host_path" >&2
    exit 1
  fi

  echo "$(date '+%Y-%m-%d %H:%M') backup ok: $host_path ($size bytes, restore verified: $restored products)"
}

backup_database wheyproof wheyproof 200000

# The UK database is created by the UK backend's first start (EF Migrate). Until
# that has happened there's nothing to back up, and that isn't a failure.
if [ "$(run_psql "SELECT 1 FROM pg_database WHERE datname = 'wheyproof_uk';" | tr -d '[:space:]')" = "1" ]; then
  backup_database wheyproof_uk wheyproof-uk 0
fi

# Both editions' files start with "wheyproof-", so one rule prunes them.
find "$TARGET" -name 'wheyproof-*.dump' -mtime +$RETENTION_DAYS -delete
