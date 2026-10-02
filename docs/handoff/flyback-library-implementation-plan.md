# Flyback Asset Library — implementation plan

## Goal

Build a small asset library for Flyback, hosted independently from the static GitHub Pages site.

- **Frontend:** existing Flyback GitHub Pages site (`https://markovcd.github.io/Flyback/`)
- **API:** Cloudflare Worker
- **Object storage:** Cloudflare R2
- **Metadata/search:** Cloudflare D1
- **Uploads:** browser uploads directly to R2 using short-lived presigned URLs
- **Consumers:** GitHub Pages frontend initially; Flyback desktop app may use the same API later

The GitHub Pages site remains static. No self-hosted server is required.

## Architecture

```text
GitHub Pages / Flyback desktop app
              |
              | HTTPS
              v
       Cloudflare Worker
          /         \
         v           v
    Cloudflare D1   Cloudflare R2
    metadata        actual files
```

For uploads, the Worker authorizes the request and returns a short-lived presigned R2 PUT URL. The browser uploads the file directly to R2, avoiding routing large files through the Worker.

For downloads, the API looks up the asset in D1 and redirects to a short-lived presigned R2 GET URL. Keep the R2 bucket private.

## Initial asset types

Support arbitrary files, with categories such as:

- Samples / audio
- Presets and patches
- Modules
- Textures and video sources
- Wavetables
- Other

Do not assume all assets are audio files. Store MIME type and size, and allow unknown MIME types.

## Suggested metadata schema

Create a D1 table named `assets` with fields along these lines:

- `id` — UUID primary key
- `filename` — user-facing filename
- `object_key` — unique R2 object key
- `content_type`
- `size`
- `category`
- `tags`
- `description`
- `author`
- `license`
- `license_url`
- `version`
- `flyback_version`
- `module_type`
- `sha256`
- `created_at`
- `status` — e.g. `pending`, `ready`, `failed`

Consider storing tags as JSON initially; use a normalized tags table or D1 FTS5 if search requirements grow. Search should only return assets whose status is `ready`.

## API outline

Public endpoints:

- `GET /api/search?q=...&category=...&limit=...&cursor=...`
  - Search filename, tags, description, author, and optionally other metadata.
  - Validate and cap pagination parameters.
  - Return only public, ready assets.
- `GET /api/assets/:id`
  - Return public metadata for one asset.
- `GET /api/download/:id`
  - Verify that the asset exists and is ready, then redirect to a short-lived presigned R2 GET URL.

Administrative endpoints (must be protected; never put an admin secret in public JavaScript):

- `POST /api/upload`
  - Validate metadata, filename, category, MIME type, declared size and allowed extensions.
  - Create a `pending` database record and return a short-lived presigned PUT URL.
  - The signed upload must be bound to the expected object key and content type.
- `POST /api/assets/:id/complete`
  - Called after the client finishes uploading.
  - Verify the R2 object exists and that its actual size matches the expected size; update the record to `ready` only after validation.
  - Consider checking a SHA-256 hash for duplicate detection and integrity.
- `DELETE /api/assets/:id`
  - Delete the R2 object and D1 row. Admin-only.
- Optional `PATCH /api/assets/:id`
  - Update editable metadata. Admin-only.

Do not implement public upload without a clear abuse-control strategy. Start with an admin-only upload flow.

## Security requirements

1. **Never ship secrets to GitHub Pages.** A token embedded in JavaScript can be extracted by any visitor.
2. Keep the R2 bucket private. Use presigned URLs for controlled downloads and uploads.
3. Store R2 S3 credentials and admin credentials as Worker secrets, never in source control.
4. Restrict R2 credentials to the `flyback-assets` bucket and the minimum required permissions.
5. Protect admin endpoints with Cloudflare Access or a properly implemented authentication flow (for example, GitHub OAuth). Do not treat CORS as authentication.
6. Configure CORS on R2 to allow only the intended frontend origin and the necessary methods/headers. A presigned URL alone does not configure browser CORS.
7. Validate all request bodies and cap request sizes. Never trust client-provided file size or MIME type without checking the stored object.
8. Rate-limit public search and download endpoints as appropriate.
9. Prevent path traversal and unsafe object keys. Generate object keys server-side, e.g. `category/<uuid>-<sanitized-filename>`.
10. Use a pending/ready state so an incomplete upload is never listed as a downloadable asset.
11. Add cleanup for abandoned pending uploads and orphaned R2 objects.
12. Decide how to moderate community submissions before enabling public uploads.

## Upload flow

1. Admin authenticates to the Worker.
2. Frontend sends metadata to `POST /api/upload`.
3. Worker validates the request, creates a `pending` D1 record, and returns a short-lived presigned PUT URL.
4. Browser uploads the file directly to R2 using that URL.
5. Frontend calls `POST /api/assets/:id/complete`.
6. Worker verifies the object and expected size (and hash if implemented), then marks the asset `ready`.
7. The public search API can now return the asset.

If the upload fails, leave the asset pending and clean it up later. Do not make the asset public before completion checks pass.

## Download flow

1. Client requests `GET /api/download/:id`.
2. Worker checks that the record exists and is `ready`.
3. Worker returns a redirect to a short-lived presigned R2 GET URL.
4. Browser or desktop app downloads the file from R2.

## Search

Start with parameterized SQL and `LIKE` queries over filename, tags, description and author. Add pagination and a sensible result limit (for example, 50–100).

For more capable full-text search, investigate D1's FTS5 support and use it if appropriate. Do not concatenate raw user input into SQL.

## Recommended repository layout

```text
flyback-library/
├── src/
│   ├── index.ts             # Worker routes and handlers
│   ├── auth.ts              # admin auth / authorization
│   ├── validation.ts        # request and metadata validation
│   └── storage.ts           # presigned URLs and R2 helpers
├── public/
│   └── admin/               # optional admin UI; must not contain secrets
├── schema.sql
├── wrangler.toml
├── package.json
├── tsconfig.json
└── README.md
```

The public library UI can live in the existing Flyback frontend or in a separate static page. Keep the API independent so the desktop application can reuse it later.

## Cloudflare setup

1. Create a Cloudflare account and enable Workers, R2 and D1.
2. Create the R2 bucket `flyback-assets`.
3. Create the D1 database `flyback-library`.
4. Add the R2 and D1 bindings to `wrangler.toml`.
5. Apply `schema.sql` to the D1 database.
6. Create bucket-scoped R2 S3 credentials for presigned URLs.
7. Store required credentials using `wrangler secret put`; never commit secrets.
8. Configure R2 CORS for the actual Flyback frontend origin.
9. Deploy with Wrangler and test the public API.
10. Configure a custom API domain if desired.

Use the current Cloudflare documentation and current Wrangler configuration format during implementation; do not assume older `wrangler.toml` examples remain current.

## Implementation phases

### Phase 1 — secure MVP

- Create Worker, R2 bucket and D1 schema.
- Implement public search, asset details and download.
- Implement authenticated admin upload using presigned URLs.
- Implement upload completion verification and pending/ready states.
- Implement admin deletion.
- Add request validation, pagination, CORS, error handling and basic rate limiting.
- Add tests for unauthorized requests, invalid metadata, incomplete uploads and missing assets.
- Document local development and deployment.

### Phase 2 — frontend

- Add a searchable asset browser to Flyback's website.
- Display filename, category, description, author, tags, size and license.
- Add download actions and loading/error/empty states.
- Add an admin-only upload form with tags, category, description, author and license fields.
- Never include admin credentials in the static frontend.

### Phase 3 — community library and desktop integration

- Add moderation and a deliberate public-submission/authentication flow before enabling community uploads.
- Add SHA-256 duplicate detection and integrity checks.
- Add previews (audio waveform/player and image/video thumbnails where appropriate).
- Add richer filtering and FTS5 search.
- Add multipart uploads for large files if required.
- Add versioning/compatibility metadata for Flyback presets and modules.
- Integrate search/download into the C#/Avalonia desktop app.

## Important implementation notes

- The earlier minimal example is a starting concept, not production-ready code. In particular, it must not expose an `ADMIN_TOKEN` in GitHub Pages, must not publish an asset before upload verification, and must configure R2 CORS correctly.
- Ensure the signed PUT request's headers exactly match those used by the browser (especially `Content-Type`).
- Verify object existence and size on completion. A client-reported size is not authoritative.
- Handle failed uploads and database/object-storage consistency. D1 and R2 do not share a single transaction.
- Use server-generated UUIDs and object keys.
- Make upload size limits configurable and verify the practical limits of the chosen upload approach. Use multipart upload when necessary.
- Add automated tests and a README with deployment steps and required secrets.

## Definition of done

- Anyone can search and download ready public assets without logging in.
- Only authenticated administrators can upload or delete assets.
- Large files upload directly to R2 rather than passing through the Worker.
- Incomplete uploads are not listed publicly.
- No credentials or admin tokens appear in the frontend bundle or Git repository.
- The same API can be used by the GitHub Pages frontend and the Flyback desktop app.
