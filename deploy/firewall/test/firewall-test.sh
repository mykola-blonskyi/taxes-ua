#!/usr/bin/env bash
# Proves cloudflare-only.sh against real packets, inside a throwaway privileged container:
#
#   docker run --rm --privileged -v "$PWD/deploy/firewall:/fw:ro" ubuntu:24.04 bash /fw/test/firewall-test.sh
#
# Add `-e IPTABLES=legacy` to run it against iptables-legacy instead of iptables-nft.
#
# The container stands in for the VPS. Network namespaces play an internet client (addresses inside and
# outside Cloudflare's ranges, IPv4 and IPv6) and a container behind a Docker-style bridge. Docker's DNAT,
# FORWARD hook and DOCKER-USER chain are recreated by hand for IPv4; on IPv6 a host listener stands in
# for docker-proxy, which is how an IPv4-only container is published on IPv6.
set -euo pipefail

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq >/dev/null
apt-get install -y -qq iptables ufw curl iproute2 python3 shellcheck >/dev/null
if [ "${IPTABLES:-nft}" = legacy ]; then
  update-alternatives --set iptables /usr/sbin/iptables-legacy >/dev/null
  update-alternatives --set ip6tables /usr/sbin/ip6tables-legacy >/dev/null
fi
echo "== $(iptables --version)"

script=/fw/cloudflare-only.sh
fixtures=/fw/test/fixtures
export CF_IPS_V4_URL=file://$fixtures/ips-v4 CF_IPS_V6_URL=file://$fixtures/ips-v6 CF_ONLY_PUBLIC_IF=pub0

failed=0
check() {
  if [ "$1" = "$2" ]; then echo "ok    $3"; else echo "FAIL  $3 (expected '$2', got '$1')"; failed=1; fi
}

echo "== shellcheck"
check "$(shellcheck "$script" "$0" >&2; echo $?)" 0 "shellcheck is clean"

cf_src=173.245.48.10
other_src=203.0.113.50
host_pub=203.0.113.1
cf_src6=2606:4700::10
other_src6=2001:db8::50
host_pub6=2001:db8::1

sysctl -qw net.ipv6.conf.all.disable_ipv6=0 net.ipv6.conf.default.disable_ipv6=0 net.ipv4.ip_forward=1
ip netns add client
ip netns add app
ip link add pub0 type veth peer name eth0 netns client
ip link add dock0 type veth peer name eth0 netns app
ip addr add $host_pub/24 dev pub0
ip addr add $host_pub6/64 dev pub0 nodad
ip link set pub0 up
ip route add $cf_src/32 dev pub0
ip route add $cf_src6/128 dev pub0
ip addr add 172.18.0.1/24 dev dock0
ip link set dock0 up
ip netns exec client sysctl -qw net.ipv6.conf.all.disable_ipv6=0
ip -n client addr add $other_src/24 dev eth0
ip -n client addr add $cf_src/32 dev eth0
ip -n client addr add $other_src6/64 dev eth0 nodad
ip -n client addr add $cf_src6/128 dev eth0 nodad
ip -n client link set eth0 up
ip -n client link set lo up
ip -n client route add 172.18.0.0/24 via $host_pub
ip -n app addr add 172.18.0.2/24 dev eth0
ip -n app link set eth0 up
ip -n app link set lo up
ip -n app route add default via 172.18.0.1

for port in 80 443 8080; do
  ip netns exec app python3 -m http.server "$port" --bind 172.18.0.2 >/dev/null 2>&1 &
done
python3 -m http.server 443 --bind :: >/dev/null 2>&1 &
ip netns exec client python3 -m http.server 9000 --bind $other_src >/dev/null 2>&1 &
for _ in $(seq 60); do
  [ "$(ip netns exec app ss -Hltn | wc -l)" = 3 ] && [ "$(ss -Hltn | wc -l)" = 1 ] \
    && [ "$(ip netns exec client ss -Hltn | wc -l)" = 1 ] && break
  sleep 0.5
done

echo "== ufw as the VPS has it: 80/443 and ssh open to anyone"
ufw --force reset >/dev/null
ufw allow 22/tcp >/dev/null
ufw allow 80/tcp >/dev/null
ufw allow 443 >/dev/null
ufw --force enable >/dev/null
ufw_rules=$(ufw show added)

echo "== Docker's chains, IPv4 published ports 80, 443 and 8080"
for cmd in iptables ip6tables; do
  $cmd -N DOCKER-USER
  $cmd -A DOCKER-USER -j RETURN
  $cmd -I FORWARD 1 -j DOCKER-USER
  $cmd -I FORWARD 2 -o dock0 -j ACCEPT
  $cmd -I FORWARD 3 -i dock0 -j ACCEPT
done
for port in 80 443 8080; do
  iptables -t nat -A PREROUTING -i pub0 -p tcp --dport "$port" -j DNAT --to-destination "172.18.0.2:$port"
done

reach() {
  if ip netns exec client curl -s -o /dev/null -m 1 --interface "$1" "http://$host_pub:$2/"; then
    echo open
  else
    echo closed
  fi
}
reach6() {
  if ip netns exec client curl -s -g -o /dev/null -m 1 --interface "$1" "http://[$host_pub6]:443/"; then
    echo open
  else
    echo closed
  fi
}
hairpin() {
  if ip netns exec app curl -s -o /dev/null -m 1 "http://$host_pub:443/"; then echo open; else echo closed; fi
}
snapshot() { { iptables-save; ip6tables-save; } | grep -v '^#' | sed 's/\[[0-9]*:[0-9]*\]//'; }

check "$(reach $other_src 443)" open "before: a non-Cloudflare client reaches 443"
check "$(reach $other_src 8080)" open "before: a non-Cloudflare client reaches published 8080"
check "$(reach6 $other_src6)" open "before: a non-Cloudflare client reaches 443 on IPv6 (docker-proxy)"

echo "== refuses to run without root"
check "$(setpriv --reuid=65534 --regid=65534 --clear-groups "$script" >/dev/null 2>&1; echo $?)" 1 "non-root apply exits 1"

echo "== a failed or bad download changes nothing"
before=$(snapshot)
bad=$(mktemp -d)
printf '' >"$bad/empty"
printf '173.245.48.0/20\nnot-an-ip\n' >"$bad/malformed"
printf '0.0.0.0/0\n' >"$bad/everything"
for case in "file:///does/not/exist" "file://$bad/empty" "file://$bad/malformed" "file://$bad/everything"; do
  status=0
  CF_IPS_V4_URL=$case "$script" >/dev/null 2>"$bad/err" || status=$?
  check "$status" 1 "v4 list $case exits 1 ($(tail -1 "$bad/err"))"
  check "$(snapshot)" "$before" "v4 list $case leaves the rules untouched"
done
status=0
CF_IPS_V6_URL=file:///does/not/exist "$script" >/dev/null 2>&1 || status=$?
check "$status" 1 "missing v6 list exits 1"
check "$(snapshot)" "$before" "missing v6 list leaves the rules untouched"

echo "== --check fails before apply"
status=0
"$script" --check >/dev/null || status=$?
check "$status" 1 "--check fails while the rules are absent"

echo "== apply"
"$script"
"$script" --check
check "$(reach $cf_src 443)" open "a Cloudflare address reaches 443"
check "$(reach $cf_src 80)" open "a Cloudflare address reaches 80"
check "$(reach $other_src 443)" closed "a non-Cloudflare address does not reach 443"
check "$(reach $other_src 80)" closed "a non-Cloudflare address does not reach 80"
check "$(reach $cf_src 8080)" closed "even Cloudflare does not reach published 8080"
check "$(reach $other_src 8080)" closed "a non-Cloudflare address does not reach published 8080"
check "$(reach6 $cf_src6)" open "a Cloudflare IPv6 address reaches 443 through docker-proxy"
check "$(reach6 $other_src6)" closed "a non-Cloudflare IPv6 address does not reach 443 through docker-proxy"
check "$(hairpin)" open "a container still reaches the host's public address on 443 (hairpin)"
check "$(ip netns exec app curl -s -o /dev/null -m 2 -w '%{http_code}' http://$other_src:9000/)" 200 \
  "a container still opens outbound connections and gets the replies"
check "$(ufw show added)" "$ufw_rules" "ufw's rules are untouched"

echo "== a ufw reload does not undo it"
ufw reload >/dev/null
check "$(reach6 $other_src6)" closed "after ufw reload a non-Cloudflare IPv6 address is still shut out"
"$script" --check >/dev/null && echo "ok    --check passes after ufw reload"

echo "== rerun is idempotent and never opens or closes the ports mid-run"
applied=$(snapshot)
probe_log=$(mktemp)
( while :; do echo "cf=$(reach $cf_src 443) other=$(reach $other_src 443) other6=$(reach6 $other_src6)"; done ) >"$probe_log" &
probe=$!
reruns=0
while [ "$(grep -c . "$probe_log")" -lt 20 ]; do "$script" >/dev/null; reruns=$((reruns + 1)); done
kill $probe
wait $probe 2>/dev/null || true
check "$(snapshot)" "$applied" "$reruns reruns leave identical rules"
check "$(grep -cv '^cf=open other=closed other6=closed$' "$probe_log" || true)" 0 \
  "all $(grep -c . "$probe_log") probes during the reruns saw Cloudflare open and others closed"
"$script" --check >/dev/null && echo "ok    --check passes after the reruns"

echo "== a Cloudflare range that disappears is removed"
grown=$(mktemp)
cat "$fixtures/ips-v4" >"$grown"
printf '\n192.0.2.0/24\n' >>"$grown"
CF_IPS_V4_URL=file://$grown "$script" >/dev/null
check "$(iptables -S CF-WEB | grep -c 192.0.2.0/24)" 1 "the extra range was applied"
status=0
"$script" --check >/dev/null || status=$?
check "$status" 1 "--check against the real list reports the extra range"
"$script" >/dev/null
check "$(iptables -S CF-WEB | grep -c 192.0.2.0/24 || true)" 0 "the stale range is gone"
check "$(snapshot)" "$applied" "rules match the earlier apply exactly"

echo "== --check notices tampering"
iptables -D CF-WEB -s 173.245.48.0/20 -j RETURN
status=0
"$script" --check >/dev/null || status=$?
check "$status" 1 "--check fails when a range is missing"
iptables -I DOCKER-USER 1 -j RETURN
ip6tables -t mangle -I INPUT 1 -j RETURN
status=0
"$script" --check >/dev/null || status=$?
check "$status" 1 "--check fails when something sits above the jumps"
"$script" >/dev/null
iptables -D DOCKER-USER -j RETURN
ip6tables -t mangle -D INPUT -j RETURN
"$script" --check >/dev/null && echo "ok    apply repairs both"

echo "== --dry-run changes nothing"
"$script" --dry-run >/dev/null
check "$(snapshot)" "$applied" "--dry-run leaves the rules untouched"

echo "== rollback from the README"
for cmd in iptables ip6tables; do
  for table in filter mangle; do
    hook=DOCKER-USER; [ "$table" = mangle ] && hook=INPUT
    $cmd -t $table -D $hook -j CF-ONLY 2>/dev/null || true
    $cmd -t $table -F CF-ONLY 2>/dev/null || true
    $cmd -t $table -F CF-WEB 2>/dev/null || true
    $cmd -t $table -X CF-ONLY 2>/dev/null || true
    $cmd -t $table -X CF-WEB 2>/dev/null || true
  done
done
check "$(reach $other_src 443)" open "after rollback a non-Cloudflare address reaches 443 again"
check "$(reach6 $other_src6)" open "after rollback a non-Cloudflare IPv6 address reaches 443 again"
check "$(snapshot | grep -c CF- || true)" 0 "after rollback no CF- chain or jump is left"

if [ "$failed" != 0 ]; then echo "SOME CHECKS FAILED"; exit 1; fi
echo "ALL PASSED"
