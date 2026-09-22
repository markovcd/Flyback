# The preset site

People submit presets to a small site on the NAS, and a render app on another machine makes a picture, a loop and a sound of each one. The two share no API for media: the render app writes files into a shared folder, and the site only reads that folder.

```
browser ──submit──▶ site (NAS, Docker) ──▶ data/presets.db
                        │  reads
                        ▼
                    media/  ◀── writes ── render app (render PC, over SMB)
                        ▲
                        └── polls /api/v1/presets?pending=true
```

## The site

Build the image from the repository root. Add `--platform linux/arm64` if the NAS has an ARM processor.

```bash
docker build -f src/Flyback.Presets.Server/Dockerfile -t flyback-presets .
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

It listens on port 8080. Put the NAS reverse proxy in front of it for HTTPS. The site trusts `X-Forwarded-For` from the proxy, so only the proxy should be able to reach port 8080.

Settings, all optional, as environment variables:

| Variable | Default | |
|---|---|---|
| `Presets__Database` | `/data/presets.db` | the SQLite file |
| `Presets__Media` | `/media` | the folder the render app writes |
| `Presets__PostsPerHour` | `20` | submissions one address may make in an hour |

## Sharing the media folder

Share `media/` over SMB so the render PC can write into it, for example as `\\nas\flyback-media`. Only the render PC's account needs write access.

## The render app

See `src/Flyback.Presets.Renderer/README.md`.

## Looking after it

- **Back up** by copying `data/presets.db` (with the site stopped, or with `sqlite3 presets.db ".backup copy.db"`) and `media/`.
- **Take a preset down** until there is moderation:

  ```bash
  sqlite3 data/presets.db "DELETE FROM presets WHERE id = '<id>'"
  rm media/<id>.*
  ```

  The id is in the preset's page address.
- **Render a preset again**: delete `media/<id>.done` or `media/<id>.failed`. The render app will pick it up on its next pass.
