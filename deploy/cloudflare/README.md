# The preset site on Cloudflare

The preset site as a Worker (ADR-0175): `worker/` answers `/api/v1`, serves the pages, the web viewer and the web editor, and keeps rows in D1 and files in R2. GitHub's machines read what is submitted with `flyback-site` and render what waits. No machine of the author's does anything for it.

```
browser ──▶ Cloudflare ──▶ Access (admin.html, /api/v1/admin/*) ──▶ Worker ──▶ D1, R2
                                                                       │
                       Validate workflow ◀── starts on each submission ┘
                       ├ checks:  flyback-site validate-submissions (service token)
                       └ renders: flyback-cli render-presets --media (no token)
                                  then flyback-site push-media (service token)
```

The Worker runs at `flybackmodular.app`; a staging copy runs at `flyback-staging.nasik2137.uk`. Secrets are set in Cloudflare and GitHub and written down nowhere here.

## Working on it

```bash
worker/run-tests.sh
```

Type-checks and runs the Worker's tests in workerd against a local D1 and R2, in the Node image the gate uses. The gate runs the same.

```bash
worker/build-assets.sh --no-aot --no-editor --no-stills
```

```bash
worker/dev.sh
```

Assembles the pages and the viewer into `worker/public`, then serves the Worker at `http://localhost:8787` with a local database. Without the flags, `build-assets.sh` compiles the viewer and editor ahead of time and draws the stills, which needs the `wasm-tools` workload; the Worker workflow builds it in Docker (`--target site-assets`).

A static asset is 25 MiB at most. A fingerprinted framework file past it, the ahead-of-time editor's runtime among them, goes to `worker/large` instead; the Worker workflow puts it in R2 through `PUT /api/v1/admin/assets/{path}`, and the Worker serves it from there on the same path.

## Setting it up, once

Run wrangler from `worker/` after `npm ci`, signed in with `npx wrangler login`.

1. **The database and the bucket.**

   ```bash
   npx wrangler d1 create flyback-site-staging
   ```

   ```bash
   npx wrangler r2 bucket create flyback-site-staging
   ```

   Put the database's id in `wrangler.jsonc` under `env.staging`, in place of the zeros, and commit it. The id is not a secret.

2. **The service tokens.** Zero Trust → Access controls → Service credentials → Create service token, twice: *flyback-github* for the workflows, and *flyback-render* only if a machine of yours is to render as well, so either can be revoked alone. Each shows its secret once.

3. **The Access application.** Zero Trust → Access controls → Applications → Add an application → Self-hosted, named *Flyback admin (staging)*, with two public hostname destinations:

   | Subdomain | Domain | Path |
   |---|---|---|
   | `flyback-staging` | `nasik2137.uk` | `admin.html` |
   | `flyback-staging` | `nasik2137.uk` | `api/v1/admin/*` |

   Two policies, made under Access controls → Policies and attached on the application's Policies tab: *Only Me* (action Allow, include Emails, the admin's address, one-time PIN), and *Machines* (action Service Auth, include Service Token, both tokens above). Service Auth is what lets a request through on a token's headers alone; Allow would send a script to a sign-in page. Copy the application's audience tag from its overview.

   `api/v1/admin` without the slash stays public: every page asks it whether the visitor is the admin. The Worker checks Access's token itself on every admin route, so a path Access was never told about is refused, not open.

4. **The Worker's settings.** Put the application's audience tag in `wrangler.jsonc` as `ACCESS_AUD` under the environment's `vars`, beside `ACCESS_TEAM_DOMAIN`. It is not a secret: every token Access signs carries it. It is the 64-character hex string the application's API answers as `aud`.

   ```bash
   npx wrangler secret put GITHUB_DISPATCH_TOKEN --env staging
   ```

   `GITHUB_DISPATCH_TOKEN` is a fine-grained GitHub token for `markovcd/Flyback` alone with *Actions: Read and write*, which is what starting a workflow takes; it can start any workflow in the repository, the Release one included. Leave it unset to let Validate's schedule pick submissions up instead, ten minutes later at most.

5. **GitHub.** Settings → Secrets and variables → Actions.

   | Secret | |
   |---|---|
   | `CLOUDFLARE_API_TOKEN` | a token from the *Edit Cloudflare Workers* template with *D1: Edit* added |
   | `FLYBACK_ACCESS_ID`, `FLYBACK_ACCESS_SECRET` | the *flyback-github* service token |

   | Variable | |
   |---|---|
   | `CLOUDFLARE_ACCOUNT_ID` | the account's id |
   | `WORKER_ENVIRONMENT` | `staging` |
   | `PRESET_SITE_URL` | `https://flyback-staging.nasik2137.uk` |

   `RELEASE_SIGNING_KEY` is already there. The Worker and Validate workflows do nothing while `WORKER_ENVIRONMENT` or `PRESET_SITE_URL` is unset.

6. **The first deploy.** Actions → Worker → Run workflow. It builds the pages, migrates the database, deploys, puts the framework files too large to be assets in R2, gives the site its default presets and plugins, and says what the web pages lack for each preset. Every push to `main` that touches the site does the same.

## Checking it

```bash
curl -s https://flyback-staging.nasik2137.uk/api/v1/presets
```

JSON, listing the defaults.

```bash
curl -sI https://flyback-staging.nasik2137.uk/admin.html
```

302 to `nasik.cloudflareaccess.com`.

```bash
curl -s -o /dev/null -w '%{http_code}\n' -H "CF-Access-Client-Id: $FLYBACK_ACCESS_ID" -H "CF-Access-Client-Secret: $FLYBACK_ACCESS_SECRET" https://flyback-staging.nasik2137.uk/api/v1/admin/unchecked
```

200 with a service token's headers, and refused without them.

Submit a preset at `/submit.html`: its page says it is being read, and within a few minutes, or ten if no dispatch token is set, it is on the shelf.

`npx wrangler tail --env staging` follows the Worker's requests live.

## Rendering

The Validate workflow's render job does it: whenever `GET /api/v1/presets?pending=true` lists anything, it builds the Dockerfile's `renderer` stage and renders the stills of at most five presets, the rest waiting for the next run. It makes no loop and no track; the pages show the still alone. The render runs a stranger's patch, so its step holds no token and writes into a folder; the next step sends the folder with `flyback-site push-media`, `done` or `failed` last. The picture is drawn on Mesa's software OpenGL, a few seconds a preset.

A machine of yours can render all three, the still, the loop and the track, uploading as it goes, with Flyback installed, ffmpeg on PATH and the *flyback-render* token in the environment:

```bash
FLYBACK_ACCESS_ID=... FLYBACK_ACCESS_SECRET=... flyback-cli render-presets --server https://flyback-staging.nasik2137.uk/
```

It checks again every five minutes; `--once`, `--limit`, `--still-only`, `--poll-minutes` and `--timeout-minutes` change that.

To render a preset again, clear its render, which puts it back in the queue:

```bash
curl -X DELETE -H "CF-Access-Client-Id: $FLYBACK_ACCESS_ID" -H "CF-Access-Client-Secret: $FLYBACK_ACCESS_SECRET" https://flyback-staging.nasik2137.uk/api/v1/admin/presets/<id>/media
```

## Production

`env.production` in `wrangler.jsonc` names a database and a bucket called `flyback-site`, and the custom domain `flybackmodular.app`, made the way steps 1 to 4 say: the Access application has that hostname as its destinations, and its audience tag is `ACCESS_AUD`. `WORKER_ENVIRONMENT` is `production` and `PRESET_SITE_URL` is `https://flybackmodular.app`.

GitHub Pages holds only `deploy/pages/redirect.html`, which sends `markovcd.github.io/Flyback/<path>` to the same path on the website.

The free plan's 10 ms of CPU a request is enough for everything but a large upload's form; check `wrangler tail` against real traffic and take the paid plan if submissions are cut off.
