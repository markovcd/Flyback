# Shared sounds and pictures on the preset site

Written on 2026-10-02, against `main` at `0c78e134`. It is on TODO.md; take it off there, and delete this file, in the commit that lands the last of it.

- **Kind:** Plan
- **Status:** Open

## The change from the first draft

The first draft was a Cloudflare Worker, R2 and D1 beside a static Pages site. Flyback already has the server that draft was reinventing: `Flyback.Server`, the preset site, in a container on the NAS behind a Cloudflare Tunnel (ADR-0131, [deploy/site/cloudflare.md](../../deploy/site/cloudflare.md)). It takes presets and plugin packages, keeps them in SQLite, rate-limits a submission per address, has an admin who publishes and deletes (Cloudflare Access guards `admin.html`), and is the one host the editor is built to ask (ADR-0133's amendment fixes `PresetSite` at build time).

So **shared sounds and pictures are a third kind of thing on that site.** No Worker, no R2, no D1, no presigned URLs, no second origin, no TypeScript. A second stack would mean a second hard-coded host in the editor, a CORS policy, a second admin sign-in and a second place every rule in `security.md` has to hold, for a feature the existing server does in the same shape as plugins.

The price is that sound files go through the NAS and its tunnel rather than R2's edge. That is the escape hatch to keep open, not to build: the store takes the file bytes behind one small interface, so R2 can sit behind it later if bandwidth ever hurts.

## What is shared

Presets and plugins are on the site already, so those are out of scope. What is left, and what the engine can use:

- **Sounds.** A WAV (ADR-0052: 8 to 32 bit PCM and 32 or 64 bit float, read by `WavReader`; a stereo file is summed to mono). This covers samples, loops and wavetables alike: a wavetable is a sound file read by a Sample.
- **Pictures.** A PNG (ADR-0059: the decoder is the engine's own).

Not shared: modules (a module is a plugin, ADR-0134), video (the engine takes none) and "other". A kind the engine cannot open is not a library entry.

Naming: the glossary's **library folder** is where a patch looks for a file it names, and `ISampleLibrary` holds those files, so *library* is taken. Call these **shared sounds** and **shared pictures**, as there are *shared presets* and *shared plugins*, and add them to the glossary in the commit that lands this.

## Decisions

**Content decides the kind, not the extension or MIME type.** The site reads a submission with the engine's own reader, as it reads a preset with `PatchIO` (`Submissions.Read`) and a plugin with `PluginPackage`. A file `WavReader` or the PNG decoder refuses is refused, with the reader's reason. Size, length and channel count come from the file, never from the form.

**What the file cannot say comes from the form.** A preset carries its own name, author and tags (ADR-0131); a WAV carries none. The form gives name, author, description, tags and license, each cleaned and cut to a length as ADR-0132 cleans a package's text. The license is required and chosen from a short fixed list: CC0, CC BY, CC BY-SA, CC BY-NC, and "my own work, free to use". A file nobody has a right to share is the real risk of this feature, and the license is what the page and the editor show next to the download.

**A submission arrives unpublished** (ADR-0133's rule, not ADR-0131's). A preset is data the author made; a sound is as likely as not somebody else's recording. Nothing is listed, served or counted until the admin publishes it, and the submitter is answered 202 with what the site read.

**Anyone may submit, rate-limited.** The first draft made upload admin-only. Here it is the same public `submit` policy presets use, since an unpublished file harms nobody and the admin is the moderation. Same-bytes twice is refused by SHA-256, as a plugin package is.

**The file goes through the server and is stored as a file.** No presigned URL, no `complete` step and no `pending` state, so there are no abandoned uploads to clean up. ADR-0131 already settled where bytes live: metadata in SQLite, files in a folder, since a blob bloats the database and cannot be streamed with seeking. The folder is `Site:Sounds` (default `/data/sounds`), beside the database and writable, unlike the read-only media share.

- The file is named by its id on disk. The submitted name is never a path; it is cleaned and used only for `Content-Disposition`.
- It is written under a temporary name and renamed, then the row is inserted. A crash between the two leaves an unreferenced file, which a sweep at start removes.
- Delete removes the row, then the file.

**Size.** The site's 20 MB request limit stays (about two minutes of 44.1 kHz stereo 16-bit). Raising it for these endpoints, as plugins' 64 MB is, is a later decision with a use for it.

**Search is the presets' search.** The same word-by-word match over name, author, description and tags, with a tag filter, a page size and `count=false`. No FTS5 until a search shows `LIKE` is the problem.

## API

Under `/api/v1`, versioned as the rest is, and shaped as `PluginApi` is so the editor reads it the same way. A `kind` of `sound` or `picture`, in the path rather than a filter, keeps each listing and each page about one thing.

Public:

- `GET /sounds?q=&tag=&license=&page=`, and the same for `/pictures`. Published only, 24 to a page.
- `GET /sounds/{id}`. One entry's metadata.
- `GET /sounds/{id}/file`. The bytes, with range requests so a browser's `<audio>` can seek, `Content-Disposition` as the cleaned name, and `count=false` to fetch without counting a download.
- `GET /tags`'s counterpart for each kind.
- `POST /sounds` and `POST /pictures`. A multipart form, rate-limited, anonymous.

Admin, behind the session the site already has (`Signed`), each answering 401 otherwise:

- `PATCH /sounds/{id}` renames or sets `published`. `DELETE /sounds/{id}`.
- Listing and fetching an unpublished entry take the same `unpublished` flag the other stores do.

Each entry carries `id`, `name`, `author`, `description`, `tags`, `license`, `fileName`, `size`, `sha256`, and what the file says about itself: for a sound its length, sample rate and channels; for a picture its width and height. Reports and ratings reuse `ReportStore` and `RatingStore` with a new kind, as plugins do.

## Where it goes

All new files, one type each, in `src/Flyback.Server/`, named as their plugin counterparts are:

- `SoundStore.cs`, `PictureStore.cs`, or one `SharedFileStore` taking the kind, if the two come out identical (they will, but for the metadata read; check before deciding), with `StoredSound` and `StoredPicture` records.
- `SoundApi.cs` and `PictureApi.cs`, mapped in `Program.cs` beside `MapPlugins`.
- `SoundSubmissions.cs`, the reader that turns bytes and a form into a checked submission or a refusal.
- A `sounds` table and a `pictures` table in the same SQLite file, made with `CREATE TABLE IF NOT EXISTS` and migrated on open, as `PresetStore` does (`saved-data.md`).
- Pages `sounds.html` and `pictures.html` in `src/Flyback.Server/wwwroot`, with the shared header, an `<audio controls>` or `<img>` per entry, license and size, and the report and rating controls the preset page has. The admin page lists unpublished ones.

Not new: authentication, rate limiting, the forwarded-header check, the tunnel, the Access policy, the Pages fallback worker and the deploy. `pages.yml` already rewrites links to server pages to `PRESETS_URL`; add the two new pages to that list. `site.yml` already builds on a change under `src/`.

## Security, as `security.md` has it

- **Secrets.** None are added. There is no new key, token or credential, which is the whole point of leaving R2's S3 keys out.
- **Sizes and numbers.** The reader caps every length it reads off the file against what the input could hold; a WAV header claiming more samples than the file has is the one to test.
- **Paths.** The id is the file's name on disk; nothing from the form or the file reaches a path.
- **Text.** Form fields are cleaned of control and bidirectional characters and cut to length before storing, and escaped where a page prints them. Parsers refuse; they do not throw.
- **Never loosened.** Nothing is served until published, and no check is skipped to make an upload work.
- **Least collected.** The rate limit counts addresses as it already does; nothing about who downloaded what is kept beyond a count.

## Tests

In `tests/Flyback.Server.Tests`, as the plugin site's are (the specs project cannot host the site, so no Gherkin scenario, as ADR-0133 says):

- A WAV and a PNG are accepted and arrive unpublished; neither is listed, served or found by its page until published.
- A file that is neither, a truncated WAV, a WAV whose header promises more than it holds, and a PNG with a lying size are refused with a reason.
- The same bytes twice are refused.
- Without the admin session, publish, rename and delete are 401.
- A request past the limit is refused; the sixth in an hour over the limit is 429.
- A name with `..`, a backslash or a device name never touches a path.
- A sound's range request returns 206 and the right slice.
- The old database, opened by the new code, still has its presets and plugins.

## Phases

1. **Server.** Stores, API, submission reader, tests, the two pages, admin listing, the ADR, the glossary entries, the `website` skill's site edit and one CHANGELOG bullet, all in the commits that land each step. Lands dark until the pages exist: a route nothing links is not visible.
2. **Editor.** A `SoundSite` beside `PluginSite` on `SiteAccess`, null in a page as plugins are. A Sample module's file picker gets "Shared sounds": search, preview, download into the library folder (Settings → Files), and the module's file set to the downloaded copy. The same for a picture's module. Never a download without the person choosing one; a download is checked against `sha256` before it is kept.
3. **Later, each on its own.** A patch that names a sound you lack offering it, as ADR-0135 does for a plugin: a patch names a path and nothing else, so this needs a hash saved with it, which is a format change and carries its upgrade step (`saved-data.md`). R2 behind the store's interface. A larger limit. Waveform previews drawn as the site's tracks are (`site-audio-tracks`).

## Open for the user

- **Server instead of Worker, R2 and D1.** Recommended, as above. Say so if the NAS's bandwidth or disk is the reason to want R2 now.
- **Sounds arrive unpublished.** Recommended, for the copyright risk. The alternative is published at once and moderated after, as presets are.
- **The license list.** Five entries above; add or cut.
- **A fixed 20 MB.** Recommended until something needs more.

## Kept from the first draft

Private-by-default until checked, a status gate before anything is public, server-made keys, validation at the door, a rate limit on the public endpoints, the admin as the only way to delete, the editor reusing the same API as the page, and moderation before any community submission is public.
