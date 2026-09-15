#!/usr/bin/env bash
# Origin lock: ports 80/443 accept traffic only from Cloudflare IP ranges.
#
# The VM is shared: WheyProof and the Turkish sister site sit behind the same
# Caddy, so this lock protects both at once.
#
# WHY IT EXISTS (measured 2026-09-05): the server's public IP (89.168.93.197)
# accepted direct requests. `curl --resolve <site-host>:443:89.168.93.197`
# returned the site and /api/stats with 200 and the response had NO `cf-ray`
# header, meaning Cloudflare was out of the path. Cloudflare's DDoS
# protection, bot checks and rate limiting rules could all be bypassed.
#
# WHAT IS NOT BYPASSED (measured): the application's own defenses still work.
# 12 fast POSTs sent straight to the origin got a 429 on the 11th request, and
# an admin endpoint returned 401 from the origin. Authentication and ASP.NET
# rate limiting don't depend on Cloudflare; this lock restores the Cloudflare
# layer on top of them.
#
# WHY DOCKER-USER AND NOT INPUT (a trap): 80/443 traffic never reaches the
# INPUT chain. Docker DNATs it in nat/PREROUTING straight to the Caddy
# container and the packet goes through FORWARD -> DOCKER-USER. The existing
# "dport 80/443 ACCEPT" rules in INPUT are effectively dead; a rule written
# there would change nothing while looking like the job was done.
#
# CERTIFICATE RENEWAL STILL WORKS (measured): DNS is proxied, so Let's Encrypt
# never connects to the origin IP; validation arrives through Cloudflare. A
# test request to /.well-known/acme-challenge/ showed up in the Caddy log with
# `remote_addr` set to a Cloudflare address.
#
# SSH (22) and WireGuard (51820/udp) are not touched by this script.
set -euo pipefail

IFACE=enp0s6
BACKUP=/root/cf-kilit-yedek.v4
CONFIRM=/run/cf-kilit-onaylandi
ROLLBACK_SECONDS=420

iptables-save > "$BACKUP"
rm -f "$CONFIRM"

# SAFETY NET: if the confirm file isn't created after the rules go in, the
# server restores the previous rules on its own. A mistake that takes the
# sites down fixes itself in 7 minutes without anyone logging in.
# (The file paths above match the copy of this script in the Turkish
# repository: both manage the same host, so they must agree.)
setsid bash -c "sleep $ROLLBACK_SECONDS; if [ ! -f $CONFIRM ]; then iptables-restore < $BACKUP; logger -t cf-kilit 'no confirmation, rolled back'; fi" </dev/null >/dev/null 2>&1 &

curl -fsS https://www.cloudflare.com/ips-v4 | tr -d '\r' | grep -E '^[0-9]' > /run/cf4.txt
COUNT=$(wc -l < /run/cf4.txt)
# If the list comes back unexpectedly short (network error, format change),
# locking with a partial list would take the sites down.
if [ "$COUNT" -lt 10 ]; then
  echo "ERROR: Cloudflare list is short ($COUNT lines), aborted" >&2
  exit 1
fi

iptables -F DOCKER-USER
while read -r cidr; do
  iptables -A DOCKER-USER -i "$IFACE" -s "$cidr" -p tcp -m multiport --dports 80,443 -j RETURN
done < /run/cf4.txt
# DROP, not REJECT: the origin shouldn't even answer scans.
iptables -A DOCKER-USER -i "$IFACE" -p tcp -m multiport --dports 80,443 \
  -m comment --comment "Cloudflare disi origin erisimi" -j DROP

echo "$COUNT Cloudflare ranges allowed, everything else dropped."
echo "WITHOUT CONFIRMATION this is rolled back after ${ROLLBACK_SECONDS} s:"
echo "  sudo touch $CONFIRM && sudo netfilter-persistent save"
