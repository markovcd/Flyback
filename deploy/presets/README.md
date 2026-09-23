# The preset site

People submit presets and plugins to a small site on the NAS, and `flyback-cli render-presets` on another machine makes a picture, a loop and a sound of each one. The two share no API for media: the render machine writes files into a shared folder, and the site only reads that folder.

```
browser ──submit──▶ site (NAS, Docker) ──▶ data/presets.db
                        │  reads
                        ▼
                    media/  ◀── writes ── flyback-cli render-presets (render PC, over SMB)
                        ▲
                        └── polls /api/v1/presets?pending=true
```

## The site

`deploy.sh` does all of this over ssh: it builds the image for the NAS's processor, loads it there, sets up the folder the first time and restarts the container.

```bash
PLUGIN_KEY=~/flyback-release.key deploy/presets/deploy.sh nas flyback-presets
```

Both arguments are optional and default to those. Set `DOCKER="sudo docker"` if the NAS needs it. `compose.yaml` is copied only when the NAS has none, so the admin's password set there survives a deploy.

`PLUGIN_KEY` is the release signing key, the PEM whose public half is `src/Flyback.App/Updates/release-key.pem`. The build packs the Figures plugin the site starts with and signs it with that key, handed in as a Docker build secret, so the image and the NAS keep no copy of it. A build without the secret starts the site without Figures.

Run from the source (the `presets` profile in Rider, or `dotnet run --project src/Flyback.Server`), a Debug build lays Figures out beside the site instead, and the site packs and signs it as it starts with a key it makes for itself at `bin/dev-data/plugin.key`, so the plugin is on the local shelf at once and a Flyback pointed at `http://localhost:8790` installs it. That key signs nothing but a developer's own builds; delete it and the next start makes another, which an editor that installed the old build then refuses as an update until the plugin is removed and installed again.

By hand: build the image from the repository root. Add `--platform linux/arm64` if the NAS has an ARM processor.

```bash
docker build -f src/Flyback.Server/Dockerfile -t flyback-presets .
```

To build on this machine and load the image on the NAS instead:

```bash
docker save flyback-presets | ssh nas docker load
```

On the NAS, put `compose.yaml` in a folder, create `data/` and `media/` next to it, and start it:

```bash
mkdir -p data media && sudo chown 1654 data
docker compose up -d
```

The container runs as user 1654, so `data/` has to be writable by that user. `media/` only needs to be readable.

It listens on port 8080. Put the NAS reverse proxy in front of it for HTTPS. The site reads the client's address from `X-Forwarded-For`, which is what the per-address rate limits count by, and believes that header only from the private ranges a proxy reaches a container over — so nothing but the proxy may be able to reach port 8080. Set `Presets__KnownProxies` if the proxy is somewhere else; anything reaching the port from an address that is not on the list is counted by the address it actually connected from.

Settings, all optional, as environment variables:

| Variable | Default | |
|---|---|---|
| `Presets__Database` | `/data/presets.db` | the SQLite file |
| `Presets__Media` | `/media` | the folder the render machine writes |
| `Presets__Defaults` | `Defaults` beside the site | the presets and plugins the site starts with |
| `Presets__Builds` | `plugins` beside the site | plugin builds to pack and sign at start, where no package of them was shipped |
| `Presets__PluginKey` | `plugin.key` beside the database | the key such a build is signed with, made there the first time |
| `Presets__PostsPerHour` | `20` | submissions one address may make in an hour |
| `Presets__ReportsPerHour` | `10` | reports one address may make in an hour |
| `Presets__RatingsPerHour` | `60` | ratings one address may give in an hour |
| `Presets__LettersPerHour` | `5` | letters one address may write in an hour |
| `Presets__KnownProxies` | the private ranges | addresses or networks, comma separated, whose `X-Forwarded-For` is believed |
| `Presets__Admin__User` | | the admin's user name |
| `Presets__Admin__Password` | | the admin's password; admin mode is off while either is blank |

## Admin mode

Set the admin's user and password in `compose.yaml` and sign in at `/admin.html`. Signed in, the shelf shows unpublished presets too, and every preset has Rename, Unpublish (or Publish) and Delete. An unpublished preset is gone from the shelf, its page and its download for everyone else, and is not rendered until it is published again.

Plugins wait for the admin: a submitted `.fbkp` is unpublished until Publish is pressed on it at `/plugins.html`, and nobody else can see or download it before then. The one the site starts with, Figures, is published from the start, like the default presets. Plugins are never rendered, and have nothing in `media/`.

Anyone can report a published preset or plugin, from its page or from the editor, with a reason and a line of detail. Reports are listed at `/admin.html` once signed in, each linking to what it is about, where it can be unpublished or deleted; Dismiss clears one, and deleting a preset or plugin clears its reports.

Letters written from inside Flyback are listed at `/admin.html` too, newest first: a mood, what was typed, an address where one was left, and the version, operating system and plugins the letter was written from. Dismiss clears one. There is no reply from here; an address is answered by mail or not at all.

A published preset or plugin is rated with one to five stars on its page, one rating per address, and rating again replaces it. Every card and the editor show the average. Only the site's own pages can rate: the endpoint takes a rating only where the browser marks the request same-origin, which the editor never does. Addresses are kept as an HMAC under a key in the database, and deleting a preset or plugin clears its ratings.

Sign-in is a cookie, kept for two weeks. Its keys live in `data/keys/`, so restarting the container does not sign the admin out. Serve the site over HTTPS, since the password crosses the wire at sign-in. Ten wrong tries from one address lock that address out for a quarter of an hour.

## Sharing the media folder

Share `media/` over SMB so the render PC can write into it, for example as `\\nas\flyback-media`. Only the render PC's account needs write access.

## Rendering

On the render PC, with Flyback installed (so `flyback-cli` has its plugins), ffmpeg on PATH (any full build: it needs libwebp, libvpx and libmp3lame) and the share mounted:

```bash
flyback-cli render-presets --server https://presets.example.org/ --media \\nas\flyback-media
```

It renders every preset still waiting, then checks again every 5 minutes. `--once` makes one pass and stops, `--poll-minutes` and `--timeout-minutes` change the waits, and `--ffmpeg` points at an ffmpeg that is not on PATH.

| File | What it is |
|---|---|
| `{id}.webp` | a 1280x720 still, four seconds in |
| `{id}.webm` | six silent seconds at 640x360 |
| `{id}.mp3` | the first thirty seconds at -16 LUFS, with a 4 s fade |
| `{id}.peaks.json` | the player's 96 bars |
| `{id}.done` | written last, once the rest are in place |
| `{id}.failed` | written instead, holding what went wrong |

A patch that wires only a picture gets no track, one that wires only a sound gets no still or loop, and a silent one gets no track. A patch using a plugin the render PC does not have is marked failed.

## Looking after it

- **Back up** by copying `data/presets.db` (with the site stopped, or with `sqlite3 presets.db ".backup copy.db"`) and `media/`.
- **Take a preset down** from admin mode: Unpublish hides it, Delete removes it. The site cannot write `media/`, so a deleted preset's files stay there until removed by hand:

  ```bash
  rm media/<id>.*
  ```

  The id is in the preset's page address.
- **Render a preset again**: delete `media/<id>.done` or `media/<id>.failed`. `render-presets` picks it up on its next pass.
