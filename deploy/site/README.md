# The site

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

The site is an image on GitHub's registry, `ghcr.io/markovcd/flyback-site`, for amd64 and arm64, in two channels:

| Tag | Pushed by | Plugins signed with |
|---|---|---|
| `latest` and `X.Y.Z` | the Release workflow, after each release | the release key |
| `dev` and `dev-<commit>` | `deploy/site/publish-dev.sh`, from this machine | the local test key |

The NAS runs one folder per channel, each holding `compose.yaml` and its own `data/` and `media/`. A `.env` beside it picks the channel and the port:

```bash
mkdir -p flyback-site/data flyback-site/media && sudo chown 1654 flyback-site/data
cp compose.yaml flyback-site/                      # latest, on 8080

mkdir -p flyback-site-dev/data flyback-site-dev/media && sudo chown 1654 flyback-site-dev/data
cp compose.yaml flyback-site-dev/
printf 'TAG=dev
PORT=8081
' > flyback-site-dev/.env
```

Start or update either one from its folder:

```bash
docker compose pull && docker compose up -d
```

The container runs as user 1654, so `data/` has to be writable by that user. `media/` only needs to be readable. `compose.yaml` holds the admin's password, so each folder's copy is its own.

To publish the dev channel, log in to the registry once with a GitHub token that has `write:packages`, then run the script:

```bash
docker login ghcr.io -u markovcd
```

```bash
deploy/site/publish-dev.sh
```

The first push makes the package private. Make it public once, under the package's settings on GitHub, so the NAS can pull without logging in.

The build packs each plugin the site starts with (the `PluginProject` items in `Flyback.Server.csproj`) and signs it with `RELEASE_SIGNING_KEY`, handed in as a Docker build secret, so the image and the NAS keep no copy of it. On GitHub that is the release key; on a developer's machine it holds a local test key, and `release-key.sh` makes one and keeps it in the user environment where there is none. A dev site's plugins therefore install only into a local build of Flyback.

Run from the source (the `presets` profile in Rider, or `dotnet run --project src/Flyback.Server`), the build lays those plugins out beside the site instead, and the site packs them again at every start, so the shelf always holds what was just built and a Flyback pointed at `http://localhost:8790` installs it. The default presets are read from the build the same way, and a changed one replaces the stored copy. A Debug run checks no keys and signs with `RELEASE_SIGNING_KEY` only where it is set; a Release run signs with it, and makes one where there is none.

To build the image here without pushing it:

```bash
docker build --secret id=release-key,env=RELEASE_SIGNING_KEY -f src/Flyback.Server/Dockerfile -t flyback-site .
```

It listens on port 8080. Put the NAS reverse proxy in front of it for HTTPS. The site reads the client's address from `X-Forwarded-For`, which is what the per-address rate limits count by, and believes that header only from the private ranges a proxy reaches a container over — so nothing but the proxy may be able to reach port 8080. Set `Site__KnownProxies` if the proxy is somewhere else; anything reaching the port from an address that is not on the list is counted by the address it actually connected from.

Settings, all optional, as environment variables:

| Variable | Default | |
|---|---|---|
| `Site__Database` | `/data/presets.db` | the SQLite file |
| `Site__Media` | `/media` | the folder the render machine writes |
| `Site__Defaults` | `Defaults` beside the site | the presets and plugins the site starts with |
| `Site__Builds` | `plugins` beside the site | plugin builds to pack at every start, where no package of them was shipped |
| `Site__PostsPerHour` | `20` | submissions one address may make in an hour |
| `Site__ReportsPerHour` | `10` | reports one address may make in an hour |
| `Site__RatingsPerHour` | `60` | ratings one address may give in an hour |
| `Site__LettersPerHour` | `5` | letters one address may write in an hour |
| `Site__KnownProxies` | the private ranges | addresses or networks, comma separated, whose `X-Forwarded-For` is believed |
| `Site__Admin__User` | | the admin's user name |
| `Site__Admin__Password` | | the admin's password; admin mode is off while either is blank |

## Admin mode

Set the admin's user and password in `compose.yaml` and sign in at `/admin.html`. Signed in, the shelf shows unpublished presets too, and every preset has Rename, Unpublish (or Publish) and Delete. An unpublished preset is gone from the shelf, its page and its download for everyone else, and is not rendered until it is published again.

Plugins wait for the admin: a submitted `.fbkp` is unpublished until Publish is pressed on it at `/shared-plugins.html`, and nobody else can see or download it before then. The ones the site starts with, Figures and Fractals, are published from the start, like the default presets. Plugins are never rendered, and have nothing in `media/`.

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

- **Roll back** by setting `TAG` in `.env` to an earlier version or `dev-<commit>`.
- **Back up** by copying `data/presets.db` (with the site stopped, or with `sqlite3 presets.db ".backup copy.db"`) and `media/`.
- **Take a preset down** from admin mode: Unpublish hides it, Delete removes it. The site cannot write `media/`, so a deleted preset's files stay there until removed by hand:

  ```bash
  rm media/<id>.*
  ```

  The id is in the preset's page address.
- **Render a preset again**: delete `media/<id>.done` or `media/<id>.failed`. `render-presets` picks it up on its next pass.
