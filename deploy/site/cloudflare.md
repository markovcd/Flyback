# Cloudflare in front of the site

How `flyback.nasik2137.uk` reaches the site on the NAS, and what guards it. Everything here is set in the Cloudflare dashboard or the NAS's `docker-compose.yaml`. The secrets (the admin password, the tunnel's token) live only on the NAS and are not written down here.

```
browser ─▶ Cloudflare ─▶ Access (admin paths only) ─▶ flyback-fallback worker ─▶ tunnel ─▶ flyback:8080 on the NAS
                                                              │
                                                              └─ site down? ─▶ markovcd.github.io/Flyback
```

## The tunnel

**Zero Trust → Networks → Tunnels → nasik2137 → Published applications**: `flyback.nasik2137.uk` to the service `http://flyback:8080`, with every additional setting at its default.

`cloudflared` and the site share the Docker network `proxy`, and the site publishes no port, so nothing but the tunnel reaches it. The tunnel sends `X-Forwarded-For` and `X-Forwarded-Proto` from the private range the site believes, so the rate limits count real visitors and the site knows it was reached over HTTPS.

The SSL/TLS encryption mode (Full, Full strict) does not apply: the tunnel carries the traffic.

## HTTPS

**nasik2137.uk → SSL/TLS → Edge Certificates → Always Use HTTPS: on.** It covers every hostname in the zone.

Cloudflare's own HSTS setting is off, since it would cover the whole zone too. The site sends its own `Strict-Transport-Security` over HTTPS.

## Caching

**nasik2137.uk → Caching → Configuration → Browser Cache TTL: Respect Existing Headers.** Left at its default, Cloudflare turns the site's `no-cache` into four hours, and a browser keeps an old `dotnet.js` asking for files the new build no longer has.

The site sends `no-cache` on every file whose name outlives a release or a render, so Cloudflare checks each with the NAS, a 304 when nothing changed, and a release needs no purge. The web viewer's fingerprinted files are the exception, kept for good; the worker below makes Cloudflare hold them.

## Access on the admin sign-in

Cloudflare Access needs the Zero Trust **Free** plan, picked once under **Choose a plan**.

**Zero Trust → Access controls → Applications → Flyback admin**, self-hosted, with two public hostname destinations:

| Subdomain | Domain | Path |
|---|---|---|
| `flyback` | `nasik2137.uk` | `admin.html` |
| `flyback` | `nasik2137.uk` | `api/v1/admin/session` |

Its policy is **Only Me**: action Allow, include Emails, the admin's own address. The login method is One-time PIN.

`api/v1/admin` stays public: every page asks it whether admin mode is on. The site's own password stays too, as the second lock. A policy with no application attached protects nothing and says nothing about it, and the password is what holds when that happens.

## The fallback worker

**Workers & Pages → flyback-fallback**, running [fallback-worker.js](fallback-worker.js). It passes every request through to the tunnel and:

- sends a browser to the GitHub Pages site when the tunnel or the container is down, to the same page where Pages has it and to its front page otherwise;
- lets `/api/` and anything but GET and HEAD fail plainly, since the editor expects JSON from those;
- caches the web viewer's fingerprinted files at Cloudflare, which caches no `.wasm` on its own: 8.6 MB per new visitor that would otherwise come from the NAS.

Under **Settings → Domains & Routes** it has the route `flyback.nasik2137.uk/*` on the zone `nasik2137.uk`, set to fail open, and its `workers.dev` URL is off. It is a route, never **Add Domain**: a custom domain would take the hostname from the tunnel.

## The NAS

The project is `/volume2/docker/flyback-site`, a `docker-compose.yaml` like [compose.yaml](compose.yaml) with these changes:

- `user:` is the NAS user's own uid and gid (`id -u`, `id -g`), and `data/` belongs to it. The UGOS permission layer on shared folders shows `data/` to user 1654 as `d---------`, whatever its owner and mode say, and neither `chown` nor `setfacl -b` changes that.
- It joins the external network `proxy`, the one `cloudflared` is on.
- `mem_limit: 1g`, and no `ports:`.

## Checking it

```bash
curl -sI https://flyback.nasik2137.uk/presets.html
```

200, with `Strict-Transport-Security`.

```bash
curl -sI http://flyback.nasik2137.uk/
```

301 to HTTPS.

```bash
curl -sI https://flyback.nasik2137.uk/admin.html
```

302 to `nasik.cloudflareaccess.com`.

```bash
curl -sI https://flyback.nasik2137.uk/viewer/_framework/dotnet.js
```

Then the same for one of the fingerprinted `.wasm` files `dotnet.js` names, twice: `CF-Cache-Status: HIT` the second time.

Stopping the container (`docker stop flyback`) sends a browser at `https://flyback.nasik2137.uk/` to the GitHub Pages site; `docker start flyback` brings it back.
