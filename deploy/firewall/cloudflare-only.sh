#!/usr/bin/env bash
# Lets only Cloudflare reach ports 80 and 443 from the public interface, and nothing reach any other
# Docker-published port from it. See deploy/firewall/README.md.
#
#   cloudflare-only.sh            apply (root)
#   cloudflare-only.sh --check    exit 0 only if the live rules match Cloudflare's current ranges (root)
#   cloudflare-only.sh --dry-run  print what apply would do, change nothing
#
# Environment overrides, for tests and unusual hosts:
#   CF_IPS_V4_URL, CF_IPS_V6_URL  where the ranges come from (file:// works)
#   CF_ONLY_PUBLIC_IF             the public interface, when the default route does not name it
set -euo pipefail

v4_url=${CF_IPS_V4_URL:-https://www.cloudflare.com/ips-v4}
v6_url=${CF_IPS_V6_URL:-https://www.cloudflare.com/ips-v6}
gate=CF-ONLY
allow=CF-WEB

mode=apply
case "${1:-}" in
  "") ;;
  --dry-run) mode=dry-run ;;
  --check) mode=check ;;
  *) echo "usage: $0 [--dry-run|--check]" >&2; exit 2 ;;
esac

die() { echo "cloudflare-only: $*" >&2; exit 1; }
say() { echo "cloudflare-only: $*"; }

if [ "$mode" != dry-run ] && [ "$(id -u)" -ne 0 ]; then
  die "must run as root (use --dry-run to only print the rules)"
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# Writes the validated list to $work/<family>. Any failure exits before a rule is touched.
fetch() {
  local family=$1 url=$2 pattern min_prefix max_prefix line count=0 prefix
  if [ "$family" = 4 ]; then
    pattern='^([0-9]{1,3}\.){3}[0-9]{1,3}/[0-9]{1,2}$'; min_prefix=8; max_prefix=32
  else
    pattern='^[0-9a-f:]+/[0-9]{1,3}$'; min_prefix=16; max_prefix=128
  fi
  curl -fsSL --proto '=https,file' --max-time 30 --retry 3 "$url" -o "$work/raw$family" \
    || die "could not download $url; nothing changed"
  : >"$work/$family"
  while IFS= read -r line || [ -n "$line" ]; do
    line=${line%$'\r'}
    [ -z "$line" ] && continue
    [[ $line =~ $pattern ]] || die "IPv$family list has a malformed line '$line'; nothing changed"
    prefix=${line#*/}
    # A short prefix would open the ports to a large part of the internet.
    if [ "$prefix" -lt "$min_prefix" ] || [ "$prefix" -gt "$max_prefix" ]; then
      die "IPv$family list has an implausible range '$line'; nothing changed"
    fi
    echo "$line" >>"$work/$family"
    count=$((count + 1))
  done <"$work/raw$family"
  [ "$count" -gt 0 ] || die "IPv$family list from $url is empty; nothing changed"
}

public_if() {
  local family=$1 dev
  if [ -n "${CF_ONLY_PUBLIC_IF:-}" ]; then echo "$CF_ONLY_PUBLIC_IF"; return; fi
  dev=$(ip -o "-$family" route show default 2>/dev/null \
    | awk '{for (i = 1; i < NF; i++) if ($i == "dev") { print $(i + 1); exit }}')
  if [ -z "$dev" ] && [ "$family" = 6 ]; then dev=$(public_if 4); fi
  [ -n "$dev" ] || die "no default route; set CF_ONLY_PUBLIC_IF; nothing changed"
  echo "$dev"
}

ipt() { if [ "$1" = 4 ]; then echo iptables; else echo ip6tables; fi; }

# Two paths reach 80/443 from outside, each gated in the chain the kernel walks for it:
#   filter/DOCKER-USER  published ports, after Docker's DNAT, matched by the original port
#   mangle/INPUT        anything that lands on the host itself, such as docker-proxy relaying IPv6
#                       to an IPv4-only container; ufw owns filter/INPUT, so the gate sits in mangle
hook() { if [ "$1" = filter ]; then echo DOCKER-USER; else echo INPUT; fi; }

tables() {
  if [ "$mode" = dry-run ] && ! command -v "$(ipt "$1")" >/dev/null; then echo filter mangle; return; fi
  if "$(ipt "$1")" -w -S DOCKER-USER >/dev/null 2>&1; then echo filter mangle; else echo mangle; fi
}

# The chain contents exactly as `iptables -t <table> -S <chain>` prints them, so --check compares text.
# The 80/443 rules use goto: a RETURN from CF-WEB resumes the hook chain, so allowed traffic carries on
# to Docker's or ufw's own rules as if the gate were not there.
rules() {
  local family=$1 table=$2 ifname=$3 cidr
  echo "-A $gate -m conntrack --ctstate RELATED,ESTABLISHED -j RETURN"
  echo "-A $gate ! -i $ifname -j RETURN"
  if [ "$table" = filter ]; then
    echo "-A $gate -p tcp -m conntrack --ctorigdstport 80 -g $allow"
    echo "-A $gate -p tcp -m conntrack --ctorigdstport 443 -g $allow"
    echo "-A $gate -p udp -m conntrack --ctorigdstport 443 -g $allow"
    echo "-A $gate -m conntrack --ctstate DNAT -j DROP"
  else
    echo "-A $gate -p tcp -m multiport --dports 80,443 -g $allow"
    echo "-A $gate -p udp -m udp --dport 443 -g $allow"
  fi
  while IFS= read -r cidr; do
    echo "-A $allow -s $cidr -j RETURN"
  done <"$work/$family"
  echo "-A $allow -j DROP"
}

# Each table is one iptables-restore commit: the chains are flushed, refilled and hooked first in
# line together, so no packet ever sees a half-built or missing gate.
restore_input() {
  local family=$1 ifname=$2 table
  for table in $(tables "$family"); do
    echo "*$table"
    echo ":$gate - [0:0]"
    echo ":$allow - [0:0]"
    echo "-F $gate"
    echo "-F $allow"
    rules "$family" "$table" "$ifname"
    if "$(ipt "$family")" -w -t "$table" -C "$(hook "$table")" -j "$gate" >/dev/null 2>&1; then
      echo "-D $(hook "$table") -j $gate"
    fi
    echo "-I $(hook "$table") 1 -j $gate"
    echo "COMMIT"
  done
}

fetch 4 "$v4_url"
fetch 6 "$v6_url"

case "$mode" in
  dry-run)
    for family in 4 6; do
      echo "# $(ipt "$family")-restore --noflush"
      restore_input "$family" "$(public_if "$family")"
    done
    ;;

  check)
    ok=yes
    if [ "$(tables 4)" = mangle ]; then say "FAIL  iptables has no DOCKER-USER (is Docker running?)"; ok=no; fi
    for family in 4 6; do
      cmd=$(ipt "$family")
      ifname=$(public_if "$family")
      for table in $(tables "$family"); do
        where="$cmd -t $table"
        chain=$(hook "$table")
        if [ "$("$cmd" -w -t "$table" -S "$chain" | grep -v '^-[NP] ' | head -1)" != "-A $chain -j $gate" ]; then
          say "FAIL  $where: $chain does not jump to $gate first"; ok=no
        fi
        if [ "$("$cmd" -w -t "$table" -S "$chain" | grep -c -- "-j $gate\$")" != 1 ]; then
          say "FAIL  $where: $chain should jump to $gate exactly once"; ok=no
        fi
        { "$cmd" -w -t "$table" -S "$gate" 2>/dev/null; "$cmd" -w -t "$table" -S "$allow" 2>/dev/null; } \
          | grep -v '^-N ' >"$work/live" || true
        rules "$family" "$table" "$ifname" >"$work/want"
        if diff -u "$work/want" "$work/live" >"$work/diff"; then
          say "ok    $where: $chain gate matches Cloudflare's ranges on $ifname"
        else
          say "FAIL  $where: rules differ from Cloudflare's current ranges (- wanted, + live)"
          cat "$work/diff"; ok=no
        fi
      done
    done
    [ "$ok" = yes ] || exit 1
    ;;

  apply)
    exec 9>/run/cloudflare-only.lock
    flock 9
    [ "$(tables 4)" != mangle ] || die "iptables has no DOCKER-USER chain (is Docker running?); nothing changed"
    for family in 4 6; do
      ifname=$(public_if "$family")
      restore_input "$family" "$ifname" >"$work/restore$family"
      "$(ipt "$family")-restore" -w --noflush <"$work/restore$family"
      say "$(ipt "$family"): $(wc -l <"$work/$family" | tr -d ' ') Cloudflare ranges on $ifname ($(tables "$family"))"
    done
    ;;
esac
