# Move Flyback.Server to Cloudflare

Written on 2026-10-02, against `main` at `3cd4ff1e`. It is on TODO.md; take it off there, and delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** In progress
- **Confirmed by reading:** `src/Flyback.Server/`, ADRs 0131, 0133, 0136, 0138, 0141, `deploy/site/`, `RenderPresetsCommand.cs`, Cloudflare's current Workers, D1 and Containers pages.

## Where it stands

Steps 2 to 5 are on `main`, dark: nothing is deployed and the NAS still serves the site. ADR-0175 records the shape, and [deploy/cloudflare/README.md](../../deploy/cloudflare/README.md) is the setup, the checks and the move, step by step.

- `worker/`: the Worker, its D1 migrations, its Vitest suite in workerd (in the gate as a Node stage), `build-assets.sh` and `dev.sh`.
- `src/Flyback.Site`: `flyback-site`, with `check-submission`, `validate-submissions [--lacks]`, `push-defaults` and `export-site`. It is a program of its own rather than commands of `flyback-cli`, because what a browser lacks is checked against the plugins the web pages link, which `flyback-cli`'s folder would then load twice. The readers moved into it, and the .NET site compiles the same files.
- Rendering moved to GitHub too, a change to this plan: a render job in the Validate workflow, at most five presets a run, with `render-presets --media` drawing into a folder in a step that holds no token and `flyback-site push-media` sending it. `render-presets` without `--media` uploads as it goes, for a machine of the author's. `--media` stays at the removal.
- `.github/workflows/validate.yml` and `worker.yml`, both off until the repository variables in the setup name the site.
- The pages call the admin's routes under `/api/v1/admin`, which the .NET site answers too. The reports and letters lists moved there as well as the changes.

Left: step 1 and step 6, which need the author's Cloudflare account, and step 7 after them.

Measured on 2026-10-05: the pages come to 292 files and 60 MB, the ahead-of-time web viewer's `dotnet.native.wasm` is 17.5 MiB, and the web editor's is 55 MiB, past the 25 MiB a static asset may be. So `build-assets.sh` moves a fingerprinted framework file that large to `worker/large`, the Worker workflow puts it in R2, and the Worker serves it from there on the same path, as this plan said it would.

The open choices below were answered with the recommendations: development dependencies locked, presets wait for Validate, the Worker dispatches with the schedule as the net, and Pages retires at the move.

## Goal

No machine of the author's serves or stores anything for the preset site. Today the NAS runs the container, a Cloudflare Tunnel reaches it, and a second PC renders every preset's media over an SMB share. After this, Cloudflare holds the state and serves every request, GitHub's hosted runners run the C# that reads a patch or a plugin package, and the author's own PC still renders, but pushes what it makes through the admin API instead of writing to a share. It listens for nothing, and the site works with it off.

The public contract does not move. A shipped editor reads `/api/v1/presets`, `/api/v1/plugins`, `/api/v1/letters` and the reports routes at a build-time host (ADR-0133's amendment), and `saved-data.md` says contracts only grow. Every route and JSON shape the editor and the pages use today stays as it is.

## What the server does, and where each part goes

| Today | After |
|---|---|
| ASP.NET app in a container (`Program.cs`, 358 lines, 2,600 across the server) | A Worker in TypeScript, one module per store as now |
| SQLite file: presets, tags, plugins, ratings, reports, letters, defaults | D1, with every file moved out to R2 |
| Preset files and plugin packages as blobs, preset files brotli-packed | R2, one object per id, stored as submitted. Packing existed to keep the database small; R2 has no such limit |
| `/media` folder on a share | R2, served by the Worker |
| Cloudflare Tunnel, `cloudflared`, the fallback Worker | Gone. Nothing is behind a tunnel to fall back from |
| Admin cookie and password, Access on `admin.html` | Cloudflare Access, with the Worker checking its signed token on every admin route |
| `submit`, `report`, `letter`, `rate`, `sign-in` rate limits per address | Same limits, in D1 (see below) |
| `Submissions.Read`, `PluginSubmissions.Read`, `BrowserPlugins.Lacking`: the engine's own readers | Run by a GitHub Actions job, not by the Worker |
| `flyback-cli render-presets` on another PC, writing to the share | The same command on the author's PC, making the files and uploading them through the admin API |
| `Defaults.Seed` at container start | A step of the Site workflow, calling the admin API |
| Web viewer and editor, the `site/` pages and `wwwroot` pages served by Kestrel | Workers static assets on the same hostname |
| Image build in `site.yml`, `deploy/site/`, `publish-dev.sh` | Deleted |

## The hard part: the C# readers

A submission is checked by the code the app opens files with: `PatchIO`, `PatchBundle`, `PluginPackage` (a zip, an ECDSA signature, and an assembly's metadata read without running it), and the module catalog that decides what a browser can open. That is the reason `Flyback.Server` is C# and references `Flyback.Engine` (ADR-0131: "to read a submission with the same `PatchIO` the app opens files with").

A Worker cannot run it: Workers are JavaScript and WebAssembly with 128 MB of memory, and the server's bundle limit alone is 128 MB unpacked. Three ways round that were considered:

- **Rewrite the readers in TypeScript.** Rejected. A second `PatchIO` and a second metadata reader drift from the first, and the repository's rule is that what the site says and what the app shows cannot disagree.
- **Run the existing image in Cloudflare Containers.** Rejected. A container's disk is not kept, so every store would still be rewritten against D1 and R2, and the container would only be a slower place to run the same Worker.
- **Compile the engine to WebAssembly and load it in the Worker** (ADR-0160 does it for browsers). Rejected for now: it is a different runtime and loader from the one that was tested, under a memory ceiling, to avoid running a job.

**Chosen: the Worker accepts and stores; a GitHub Actions job validates with the real C#.** It is the shape `render-presets` already has: the site lists what is waiting, a machine with Flyback on it does the work and reports back.

### A submission's life

1. `POST /api/v1/presets` or `/plugins`. The Worker checks only what is cheap and certain: the form, the field names, the size (20 MB, 64 MB for a package), and the first bytes (a zip's signature, or JSON for a `.fbk`). It writes the file to R2 under a fresh id, adds a D1 row with `status = 'unchecked'`, and answers **202** with the id.
2. An `unchecked` row is listed nowhere, served to nobody and counted nowhere, for presets as for plugins. This is a change for presets, which show up at once today; see the open decisions.
3. The Worker asks GitHub to run the **Validate** workflow (`repository_dispatch`). A scheduled run every few minutes is the fallback if the dispatch is lost.
4. Validate lists `unchecked` rows through the admin API, downloads each, and runs a new `flyback-cli check-submission` that does exactly what `Submissions.Read` and `PluginSubmissions.Read` do now and prints one JSON document: refused with a reason, or accepted with name, author, description, tags and, for a plugin, everything the package says about itself, plus what the web pages lack (`BrowserLack`).
5. Validate `PUT`s the result back. An accepted preset becomes `published` and joins the render queue; an accepted plugin becomes `unpublished`, as ADR-0133 has it. A refused file becomes `refused` with its reason, and the submitter, who has only the id, can read the reason at `GET /api/v1/presets/{id}`; the file is deleted from R2 after a week.

The decompile-and-read rule holds: Validate reads a package and never runs it (`security.md`, `/review-plugin`).

## Data

D1 holds rows and nothing larger than a row can hold (its limit is 2 MB a row, and a package is up to 64 MB). R2 holds every file.

- Tables port as they are, with a `status` column added to presets and plugins and an `r2_key` or the id as the key. `PresetStore`'s migrate-on-open becomes numbered migrations in `migrations/`, applied by Wrangler.
- `fold(column)` is a custom SQLite function the .NET server registers, and D1 has none. Each searchable table gets a `search` column, folded and lowercased when written, and the word search matches against it.
- D1 binds at most 100 parameters a query and runs 1,000 queries per invocation on the paid plan (50 on free); a listing page is one query plus one for its ratings, so neither bites.
- Rate limits are a `limits(visitor, policy, window, count)` table with an upsert per request. Cloudflare's rate-limiting binding only counts in 10- or 60-second periods, which cannot say "20 an hour". The visitor is `CF-Connecting-IP` with `Visitor.Of`'s IPv6 `/64` rule; Cloudflare sets that header, so `KnownProxies` and the forwarded-header code have no counterpart.
- Ratings, reports and letters keep their rules.

## The one-time move

A command in `flyback-cli`, not a script, since the files must be unpacked (`Packing.Unpack`) and their ids kept: `flyback-cli export-site --db presets.db --out <folder>` writes each preset and package as `<id>` files plus a `rows.sql` for D1, and the media folder as it is. Wrangler's `d1 execute` and R2 uploads load them. Ids survive, so every link, rating, and kept shared preset in an editor still resolves. The command runs once and is deleted with the server.

## API changes

Public routes and shapes: unchanged, with these differences a client can see: a submission answers 202 rather than 201 with the preset, and an entry carries `status`.

Admin moves under **`/api/v1/admin/...`**: sign-in and sign-out go (Access is the sign-in), `PATCH` and `DELETE` of a preset, plugin, report and letter take the new prefix, and so do the unchecked list and the result `PUT`. Access can only gate by path, and today's admin `PATCH /presets/{id}` shares a path with the public `GET`. Only `admin.html` and the preset pages call them.

`GET /api/v1/admin` stays public and says whether the caller is the admin, since every page asks it.

## Who may do what

- **Access** guards `admin.html` and `/api/v1/admin/*`, with two policies: the author's address by one-time PIN, and a **service token** for the Validate and Site workflows and for `render-presets` on the author's PC.
- **The Worker verifies the token itself** (`Cf-Access-Jwt-Assertion`: signature against the team's keys, audience and expiry) on every admin route, and answers 401 without it. The deployment note's warning that an Access application with no policy "protects nothing and says nothing about it" stops being a risk, and the password as a second lock is replaced by a check that holds when Access is misconfigured.
- Secrets are Worker secrets and GitHub Actions secrets (the service token, a fine-scoped token that can only start the Validate workflow, the release key where a plugin is signed). None is written down in the repository, and the Worker answers every request without printing any.

## Pages, the viewer and the editor

`site/`, the server's `wwwroot` pages, the web viewer and the web editor are static files, so they become Workers static assets on the same hostname. The pages call `/api/v1` relative to their own origin and need no change for that.

Cloudflare compresses at the edge, so `Precompressed.cs`, `StaticCache.cs` and the worker that makes Cloudflare cache the `.wasm` go away. A static asset may be 25 MiB at most. The editor's un-ahead-of-time `dotnet.native.wasm` is 10 MB in the Release build here; the image's ahead-of-time build is larger and **has not been measured**. If one passes the limit, the file goes to R2 behind the same route.

## Defaults and the render

**Defaults** (ADR-0138, 0141): the Site workflow, after building, signs the plugin packages with the release key and calls `PUT /api/v1/admin/defaults/{fileName}` with each file and its hash. The route keeps the rules: the same file twice changes nothing, a changed file replaces the stored one under its id, one the admin deleted stays deleted. `PresetSiteDefaultsTests` stays the check that a default opens.

**Lacks:** which plugins the web pages cannot open depends on the viewer build, not the preset. Validate stores it per preset, and the Site workflow runs a pass that refreshes every preset's after a deploy.

**Render stays on the author's PC**, which has the CPU, the GPU and ffmpeg that ADR-0131 wanted, and which the site never reaches. `flyback-cli render-presets` keeps its loop and its rule that a patch which does not open whole is marked failed, and changes only where it reads and writes:

- `--server` is the site's address and is all it needs to find work: it lists what is waiting at `GET /api/v1/presets?pending=true`, which stays public as now.
- `--media <folder>` goes. The command renders into a temporary folder it owns and deletes, makes `{id}.webp`, `.webm`, `.mp3`, `.peaks.json`, then `{id}.done` or `{id}.failed` with the reason, exactly the files `MediaWriter` makes today.
- Each file is sent to a new admin route, `PUT /api/v1/admin/presets/{id}/media/{name}`, into R2. `done` goes last, so the page never shows a half-made render, and `failed` carries its text. The route accepts only those names and refuses any other, since the name reaches an object key.
- The service token comes from two environment variables, `FLYBACK_ACCESS_ID` and `FLYBACK_ACCESS_SECRET`, sent as Access's `CF-Access-Client-Id` and `CF-Access-Client-Secret`. The command reads them and never prints them, and with neither set it says so and stops. A token the author's machine holds can only call `/api/v1/admin/*`; revoking it in Cloudflare cuts the PC off.
- `--once` and `--poll-minutes` stay. Deleting a preset's `{id}.done` to render it again becomes `DELETE /api/v1/admin/presets/{id}/media`, which puts it back in the queue.

This lands the TODO item that says rendering should not reach into a shared folder.

## Tests

`Flyback.Server.Tests` goes with the server. Its cases move to the Worker's tests, run by Vitest in Cloudflare's own Workers pool, which runs a Worker against a local D1 and R2: the listing and filters, unpublished stays hidden, the 20 and 64 MB limits, an unchecked row is invisible, admin routes refuse a request with no valid token, ratings are one each, the `/64` rule, and the old database's rows load after migration.

Two C# tests stay or are added: the editor's `PresetSite` and `PluginSite` clients against the contract's recorded responses, and `check-submission` against the cases the server's tests have for the readers. The gate (`docker build --target gate .`, ADR-0120) runs the Worker's tests too, so the image gains a Node stage.

## Order of work

Each step lands on `main` and leaves `main` releasable. Nothing serves from the new Worker until the cutover; it runs on a staging route, as the fallback Worker does under the same zone.

1. **Cloudflare, once.** The D1 database, the R2 bucket, the Access application and service token, a staging route. The setup is written down in `deploy/cloudflare/README.md`, replacing `deploy/site/cloudflare.md`.
2. **The read API.** The Worker with presets, plugins, tags, ratings, reports and letters, reading migrated data, and its tests. `export-site` lands here.
3. **Submissions.** Accept, 202, the unchecked state, `check-submission`, the Validate workflow, the admin routes and the Access check.
4. **Pages.** The static assets, the viewer and the editor, on staging.
5. **Render and defaults.** The media routes, `render-presets` uploading through them, and the defaults call.
6. **Cutover.** Run `export-site` against the live NAS data, point `flyback.nasik2137.uk` at the Worker, check the editor's gallery and plugins window against it, and stop the container.
7. **Removal**, in its own commit: `src/Flyback.Server`, `tests/Flyback.Server.Tests`, `site.yml`'s image build, `deploy/site/`, the fallback Worker, `publish-dev.sh`, the self-hosted pieces named in the pipeline rule that only the site used. A new ADR supersedes 0131, 0133, 0136, 0138 and 0141 where they name the NAS, the share and the container, and the glossary, the engineering guide, `README.md`, `SECURITY.md` and the `website` and `preset-site-defaults` skills are updated in the same commit. The ADR is numbered from `main` at commit time (the `adrs` skill).

Shared sounds and pictures, the feature this file started as, then land as a third store on the Worker, with the decisions made earlier: WAV and PNG only, unchecked then unpublished until the admin publishes, a license from a short list, stored as an R2 object by id. They are a later item, not part of the move.

## Limits that matter, as the docs state them today

| | Free | Paid |
|---|---|---|
| Worker CPU per request | 10 ms | 30 s by default |
| Memory | 128 MB | 128 MB |
| Request body | 100 MB | 100 MB |
| D1 database | 500 MB | 10 GB, fixed |
| D1 row or blob | 2 MB | 2 MB |
| Subrequests per invocation | 50 | 10,000 |

With validation off the Worker, a request is a few milliseconds, so the free plan may be enough. Check it against real traffic before the cutover, and take the paid plan if it is not.

## Open for the user

- **The npm toolchain.** The repository takes no package it did not write for the engine, and `security.md` calls a new dependency a question. A Worker needs Wrangler, TypeScript and Vitest at build time. Recommended: development dependencies only, locked in a committed `package-lock.json`, restored with `npm ci` in the gate, and no runtime dependency in the Worker.
- **Presets appear after a few minutes**, not at once, since they wait for Validate. The alternative is to parse the name, author and tags from the `.fbk`'s JSON in the Worker and list at once, with Validate refusing it afterward; that is a second reader of the text cleaning rules. Recommended: wait.
- **How Validate is started.** Recommended: the Worker dispatches it, with a token that can only run workflows in this repository, plus a scheduled run as the net. The alternative is the schedule alone, with no token in Cloudflare and a delay of five minutes or more.
- **GitHub Pages.** `pages.yml` still publishes `site/` and rewrites links to the preset site's pages. Recommended: retire it at the cutover and serve everything from the Worker; the rewrite and `PRESETS_URL` go with it.
