# Cloudflare-only firewall

`cloudflare-only.sh` makes the VPS answer on ports 80 and 443 to Cloudflare and nobody else, and stops
the internet from reaching any other port a Docker container publishes. The site keeps working through
Cloudflare. A request sent straight to the origin's address times out (#260).

This file and the script are deliberately generic. Do not add the server's address, its open ports or
its container names here, because the repository is public.

## Why UFW alone is not enough

Docker publishes a container port by DNAT in the `nat` table. Those packets are forwarded to the
container and never pass UFW's `INPUT` rules, so a published port is open to the world whatever UFW
says. Docker's documented place for user rules is the `DOCKER-USER` chain, which runs before Docker's
own forwarding rules. By the time a packet reaches it, DNAT has already rewritten the destination, so
the rules match the port the client asked for with `-m conntrack --ctorigdstport`.

Sources read for this change:

- Docker, [Packet filtering and firewalls](https://docs.docker.com/engine/network/packet-filtering-firewalls/),
  "Docker and ufw". Published ports bypass UFW.
- Docker, [Docker with iptables](https://docs.docker.com/engine/network/firewall-iptables/), "Add iptables
  policies before Docker's rules" (use `DOCKER-USER`) and "Match the original IP and ports for requests"
  (`conntrack --ctorigdstport`, with `--ctstate RELATED,ESTABLISHED` for replies).
- Docker, [Install Docker Engine on Debian](https://docs.docker.com/engine/install/debian/), "Firewall
  limitations". Use `iptables`/`ip6tables` (nft or legacy backend), not raw `nft` rules.

## What it installs

Two paths reach 80/443 from outside, and the script gates each one where the kernel walks it, for both
`iptables` and `ip6tables`.

**Published ports, in `filter` → `DOCKER-USER`.** This is the path Traefik's 80/443 take.

```
DOCKER-USER  1  -j CF-ONLY                                   (always the first rule)

CF-ONLY      replies to existing connections                 RETURN
             anything not arriving on the public interface   RETURN   (loopback, Docker bridges)
             original port tcp/80, tcp/443, udp/443          goto CF-WEB
             any other connection to a published port        DROP     (ctstate DNAT)

CF-WEB       one RETURN per Cloudflare range, then           DROP
```

**The host itself, in `mangle` → `INPUT`.** This catches anything that lands on the host instead of
being forwarded: a process listening on the host, and IPv6 when `docker-proxy` relays it to an
IPv4-only container. It has the same shape, but matches `--dports 80,443` and has no catch-all drop, so
UFW still decides every other host port (SSH included).

A `RETURN` from `CF-WEB` hands the packet back to the hook chain and on to Docker's or UFW's own rules,
exactly as if the script were not there. The public interface is the one the default route uses
(override it with `CF_ONLY_PUBLIC_IF=<name>`). Traffic from Docker bridges is never gated. A container
that calls one of this VPS's DNS-only hostnames, such as Coolify uploading backups to the MinIO behind
Traefik, keeps working.

Each table is one `iptables-restore --noflush` commit, so the chains are flushed, refilled and hooked in
first place together. A rerun never has a moment where 80/443 are open to everyone or closed to
Cloudflare. The test below probes the ports continuously through hundreds of reruns to show it.

The lists come from `https://www.cloudflare.com/ips-v4` and `ips-v6`. The script exits non-zero before
touching any rule if a download fails, a list is empty, a line is not a CIDR, or a range is implausibly
wide (shorter than /8 for IPv4, /16 for IPv6).

### Why it leaves UFW's rules alone

The script does not narrow UFW's `allow 80/443` rules. It filters the host path in `mangle` instead, for
two reasons:

- UFW owns `filter` → `INPUT` and rebuilds its chains on `ufw reload`. A gate in `mangle` runs before
  UFW and survives its reloads; the test reloads UFW to prove it.
- A UFW rule limited to Cloudflare's sources would also shut out containers reaching the VPS's own
  public address on 443 through `docker-proxy` (hairpin). That is how Coolify reaches a DNS-only
  hostname served on the same VPS. The `mangle` gate only looks at the public interface.

So after this, `ufw status` still shows 80/443 allowed from anywhere, and that is expected. From the
internet, the gate drops everything except Cloudflare before UFW sees it.

## Before installing

- **Only Cloudflare-proxied hostnames keep working from outside.** Anything reached as
  `http(s)://<origin-ip>` or through a DNS-only (grey cloud) record stops answering on 80/443 for clients
  off the VPS. Containers on the VPS can still use such hostnames.
- **Every other published port closes to the internet.** If you open the Coolify dashboard, or any other
  container, as `<origin-ip>:<port>`, that stops working. Reach it through an SSH tunnel
  (`ssh -L <port>:localhost:<port> <host>`, then open `http://localhost:<port>`), or put it behind a
  Cloudflare-proxied hostname routed by Traefik. SSH is a host service and is not affected.
- **Certificates.** Traefik gets its certificates through the Cloudflare DNS challenge
  (`docs/deploy.md`). That needs no inbound HTTP, so renewals keep working.
- **Development and test containers must not publish on `0.0.0.0`.** Publish them on loopback
  (`-p 127.0.0.1:8080:80`, in Compose `"127.0.0.1:8080:80"`) or not at all. The firewall drops outside
  traffic to them anyway, but a port that is never published needs no firewall to stay shut.

## Install

On the VPS, from a checkout or a copy of this folder:

```bash
sudo install -m 0755 deploy/firewall/cloudflare-only.sh /usr/local/sbin/cloudflare-only.sh
sudo /usr/local/sbin/cloudflare-only.sh --dry-run      # read what it will do
sudo /usr/local/sbin/cloudflare-only.sh                # apply
sudo /usr/local/sbin/cloudflare-only.sh --check        # every line should start with "ok"
sudo install -m 0644 deploy/firewall/cloudflare-only.service deploy/firewall/cloudflare-only.timer \
  /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable cloudflare-only.service cloudflare-only.timer
sudo systemctl start cloudflare-only.timer
```

Keep the SSH session open until the outside checks below pass.

The service is `WantedBy=docker.service` and ordered after it, so it reapplies the rules whenever Docker
starts: at boot, after a daemon restart, after an upgrade. Between Docker starting and the service
finishing (a few seconds at boot) the old behaviour applies. The timer reruns it weekly to pick up
changes to Cloudflare's ranges. A failed run retries every minute. See past runs with
`journalctl -u cloudflare-only.service` and the next one with `systemctl list-timers cloudflare-only.timer`.

## Verify from outside

From a machine that is neither the VPS nor behind Cloudflare:

```bash
# Through Cloudflare: works.
curl -sS -o /dev/null -w '%{http_code}\n' https://taxes.blonskyi.dev/

# Straight to the origin: must time out (curl exit 28), not be refused or answer.
curl -sS -o /dev/null -m 10 --resolve taxes.blonskyi.dev:443:<origin-ip> https://taxes.blonskyi.dev/; echo "exit $?"
curl -sS -o /dev/null -m 10 http://<origin-ip>/; echo "exit $?"

# Any other port a container publishes: must time out too.
nc -vz -w 5 <origin-ip> <port>
```

If the VPS has a public IPv6 address, repeat the direct checks against `[<origin-ipv6>]`.

On the VPS, `sudo cloudflare-only.sh --check` compares the live rules with Cloudflare's current lists, and
`sudo iptables -L CF-WEB -v -n` shows how much traffic each range carries.

## Rollback

```bash
sudo systemctl disable --now cloudflare-only.timer cloudflare-only.service
for cmd in iptables ip6tables; do
  sudo $cmd -t filter -D DOCKER-USER -j CF-ONLY
  sudo $cmd -t mangle -D INPUT -j CF-ONLY
  for table in filter mangle; do
    sudo $cmd -t $table -F CF-ONLY; sudo $cmd -t $table -F CF-WEB
    sudo $cmd -t $table -X CF-ONLY; sudo $cmd -t $table -X CF-WEB
  done
done
sudo rm /etc/systemd/system/cloudflare-only.service /etc/systemd/system/cloudflare-only.timer \
  /usr/local/sbin/cloudflare-only.sh
sudo systemctl daemon-reload
```

When Docker's `ip6tables` support is off, `ip6tables` has no `DOCKER-USER` and its `filter` lines print
errors. Those errors are harmless. UFW was never changed, so it needs no rollback.

## Test

`test/firewall-test.sh` proves the script with real packets inside a throwaway privileged container, so
the machine running Docker is never touched:

```bash
docker run --rm --privileged -v "$PWD/deploy/firewall:/fw:ro" ubuntu:24.04 bash /fw/test/firewall-test.sh
docker run --rm --privileged -e IPTABLES=legacy -v "$PWD/deploy/firewall:/fw:ro" ubuntu:24.04 bash /fw/test/firewall-test.sh
```

It recreates Docker's DNAT and `DOCKER-USER` hook, runs UFW with 80/443 open, and puts a client in a
network namespace with addresses inside and outside Cloudflare's ranges on IPv4 and IPv6. It checks
that:

- Cloudflare reaches 80/443 and other addresses do not, both forwarded and through a host listener.
- No one reaches another published port.
- Containers still connect out, and still reach the host's public address (hairpin).
- Failed or bad downloads change nothing.
- Reruns are identical and never open or close the ports mid-run.
- Stale ranges are removed.
- `--check` catches drift, and `--dry-run` changes nothing.
- `ufw reload` does not undo the gate.
- The rollback above reopens the ports.

`test/fixtures/` holds a copy of Cloudflare's lists.
