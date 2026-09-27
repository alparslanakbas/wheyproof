#!/usr/bin/env bash
# The deploy steps on the server. GitHub Actions does NOT run this directly:
# the deploy key can only run the deploy gate on the server
# (/usr/local/bin/deploy-kapisi, kept in the Turkish repo as
# scripts/deploy-kapisi.sh); the gate updates this checkout (git pull) and runs
# this script from the current commit. Until 27 Sept these steps were written
# inside the workflow; they were moved here unchanged.
set -e
cd "$(dirname "$0")/.."

docker compose build

# --force-recreate is required: "up -d" alone can keep the old
# container even after the image was rebuilt, serving old code while
# the workflow looks green.
# --no-deps keeps the database running across deploys.
# --remove-orphans deletes containers of services that no longer
# exist. The services were once named backend/frontend; a leftover
# container would keep the "backend" alias on web-edge, where the
# Turkish Caddy looks for its own backend.
# --wait blocks until the db healthcheck passes. --no-deps below skips
# the service_healthy condition, so without this the backend raced a
# fresh database on the first deploy, crashed on "connection refused"
# and only came up after Docker restarted it.
docker compose up -d --wait db
# The wheyproof-uk-* pair is the UK section (www.wheyproof.com/uk), the
# same app run a second time; see docker-compose.yml.
docker compose up -d --force-recreate --no-deps --remove-orphans \
  wheyproof-backend wheyproof-frontend wheyproof-uk-backend wheyproof-uk-frontend

# THE EDGE BELONGS TO THE TURKISH STACK. Its Caddy imports
# caddy/sites/*.caddy from this repo, so a change here needs a reload
# there. Validate first: an invalid config would otherwise take BOTH
# sites down on the next Caddy restart. Reload itself is atomic and
# keeps the old config if the new one fails.
CADDY=$(docker ps -q \
  --filter "label=com.docker.compose.project=protein-avcisi" \
  --filter "label=com.docker.compose.service=caddy")
if [ -z "$CADDY" ]; then
  echo "ERROR: the shared Caddy container isn't running."
  exit 1
fi
docker exec "$CADDY" caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
docker exec "$CADDY" caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile

# Prove the deploy really works, from INSIDE the shared network (from
# outside, the Cloudflare cache could answer instead). The probe runs
# in a throwaway busybox container on web-edge, using the same aliases
# Caddy uses. The Host header is required: Angular SSR rejects hosts
# not in NG_ALLOWED_HOSTS.
echo "Waiting for the services..."
for i in $(seq 1 36); do
  if docker run --rm --network web-edge busybox:1.36 \
       wget -q -O /dev/null -T 5 http://wheyproof-backend:8080/api/stats \
     && docker run --rm --network web-edge busybox:1.36 \
       wget -q -O /dev/null -T 5 --header "Host: www.wheyproof.com" http://wheyproof-frontend:4000/ \
     && docker run --rm --network web-edge busybox:1.36 \
       wget -q -O /dev/null -T 5 http://wheyproof-uk-backend:8080/api/stats \
     && docker run --rm --network web-edge busybox:1.36 \
       wget -q -O /dev/null -T 5 --header "Host: www.wheyproof.com" http://wheyproof-uk-frontend:4000/uk/; then
    echo "Verified: US and UK backends and frontends respond."
    exit 0
  fi
  sleep 5
done

echo "ERROR: the services didn't respond within 3 minutes."
docker compose ps
docker compose logs --tail 60 wheyproof-backend wheyproof-frontend wheyproof-uk-backend wheyproof-uk-frontend
exit 1
