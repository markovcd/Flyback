import { dispatchValidate } from "./dispatch";
import type { Env } from "./env";
import { attachment, badRequest, body, field, flag, json, noContent, notFound, refused, whole } from "./http";
import { mediaFile, presetFile } from "./keys";
import { limited } from "./limits";
import { MEDIA_FILES, mediaOf } from "./media";
import { forgetRatings, NO_RATING, type Rating, ratingOf, ratingsOf } from "./ratings";
import { forgetReports } from "./reports";
import { anywhere, baseName, extension, folded, named, newId, now, SEPARATOR, stem, words } from "./text";

/** Kestrel's limit on the whole request, which the .NET site held a preset to. */
export const PRESET_LIMIT = 20 * 1024 * 1024;

const PAGE_SIZE = 24;
const PENDING_LIMIT = 50;
const TAG_LIMIT = 60;

export interface PresetRow {
  id: string;
  name: string;
  author: string | null;
  description: string | null;
  file_name: string;
  size: number;
  submitted_at: string;
  downloads: number;
  published: number;
  status: "unchecked" | "checked" | "refused";
  reason: string | null;
  lacks: string | null;
  media_state: string;
  media: string;
  media_rev: number;
  peaks: string | null;
  tag_list: string | null;
}

const COLUMNS = `p.*, (SELECT group_concat(t.tag, char(31)) FROM preset_tags t WHERE t.preset_id = p.id) AS tag_list`;

/** What anyone may see: checked, and published unless the admin is asking. */
const visible = (admin: boolean): string => (admin ? "p.status = 'checked'" : "p.status = 'checked' AND p.published = 1");

export function presetView(row: PresetRow, rating: Rating) {
  return {
    id: row.id,
    name: row.name,
    author: row.author,
    description: row.description,
    tags: row.tag_list ? row.tag_list.split(SEPARATOR).sort() : [],
    fileName: row.file_name,
    size: row.size,
    submitted: row.submitted_at,
    downloads: row.downloads,
    published: row.published !== 0,
    file: `/api/v1/presets/${row.id}/file`,
    media: mediaOf(row),
    rating: { average: rating.average, count: rating.count },
    lacks: row.lacks ? (JSON.parse(row.lacks) as unknown) : null,
    status: row.status,
    reason: row.reason,
  };
}

export async function findPreset(env: Env, id: string, where = "1 = 1"): Promise<PresetRow | null> {
  return env.DB.prepare(`SELECT ${COLUMNS} FROM presets p WHERE p.id = ? AND ${where}`).bind(id).first<PresetRow>();
}

/** Whether the preset is there for anyone to see, report and rate. */
export const presetShown = async (env: Env, id: string): Promise<boolean> =>
  (await findPreset(env, id, visible(false))) !== null;

async function views(env: Env, rows: PresetRow[]) {
  const rated = await ratingsOf(env, "preset", rows.map((r) => r.id));
  return rows.map((r) => presetView(r, rated.get(r.id) ?? NO_RATING));
}

/** GET /presets: newest first, matching all of q's words and tag; or with pending=true, what waits for a render. */
export async function listPresets(env: Env, url: URL, admin: boolean): Promise<Response> {
  const pending = flag(url, "pending");
  const page = whole(url, "page");
  if (pending === "bad" || page === "bad") return badRequest("A page is a whole number, and pending is true or false.");

  if (pending === true) {
    const { results } = await env.DB.prepare(
      `SELECT ${COLUMNS} FROM presets p
       WHERE p.status = 'checked' AND p.published = 1 AND p.media_state = 'pending'
       ORDER BY p.submitted_at, p.id LIMIT ?`,
    )
      .bind(PENDING_LIMIT)
      .all<PresetRow>();

    return json({ items: await views(env, results), total: results.length, page: 1, pageSize: PENDING_LIMIT });
  }

  const at = Math.max(1, page ?? 1);
  const where = [visible(admin)];
  const values: unknown[] = [];

  for (const word of words(url.searchParams.get("q"))) {
    where.push("p.search LIKE ? ESCAPE '\\'");
    values.push(anywhere(word));
  }

  const tag = url.searchParams.get("tag")?.trim();
  if (tag) {
    where.push("EXISTS (SELECT 1 FROM preset_tags t WHERE t.preset_id = p.id AND t.tag = ?)");
    values.push(tag.toLowerCase());
  }

  const filter = "WHERE " + where.join(" AND ");
  const [counted, listed] = await env.DB.batch<{ n: number } | PresetRow>([
    env.DB.prepare(`SELECT count(*) AS n FROM presets p ${filter}`).bind(...values),
    env.DB.prepare(
      `SELECT ${COLUMNS} FROM presets p ${filter} ORDER BY p.submitted_at DESC, p.id DESC LIMIT ? OFFSET ?`,
    ).bind(...values, PAGE_SIZE, (at - 1) * PAGE_SIZE),
  ]);

  const total = (counted!.results[0] as { n: number }).n;

  return json({ items: await views(env, listed!.results as PresetRow[]), total, page: at, pageSize: PAGE_SIZE });
}

/**
 * GET /presets/{id}. A submission still being checked, or refused, is answered to
 * whoever has its id, which only its submitter does, so they can read why.
 */
export async function getPreset(env: Env, id: string, admin: boolean): Promise<Response> {
  const row = await findPreset(env, id);
  if (row === null || (row.status === "checked" && row.published === 0 && !admin)) return notFound();
  return json(presetView(row, await ratingOf(env, "preset", id)));
}

/** GET /presets/{id}/file, counted as a download unless count=false, as render-presets asks. */
export async function presetFileOf(env: Env, url: URL, id: string, admin: boolean): Promise<Response> {
  const count = flag(url, "count");
  if (count === "bad") return badRequest("count is true or false.");

  const row = await findPreset(env, id, visible(admin));
  if (row === null) return notFound();

  const file = await env.FILES.get(presetFile(id));
  if (file === null) return notFound();

  if (count !== false) await env.DB.prepare("UPDATE presets SET downloads = downloads + 1 WHERE id = ?").bind(id).run();

  return download(file, row.file_name);
}

export const download = (file: R2ObjectBody, fileName: string): Response =>
  new Response(file.body, {
    headers: {
      "Content-Type": "application/octet-stream",
      "Content-Disposition": attachment(fileName),
      "Content-Length": String(file.size),
    },
  });

/** GET /tags: the most used, with how many presets carry each. */
export async function listTags(env: Env, admin: boolean): Promise<Response> {
  const { results } = await env.DB.prepare(
    `SELECT t.tag AS tag, count(*) AS count FROM preset_tags t JOIN presets p ON p.id = t.preset_id
     WHERE ${visible(admin)} GROUP BY t.tag ORDER BY count DESC, t.tag LIMIT ?`,
  )
    .bind(TAG_LIMIT)
    .all();

  return json(results);
}

/**
 * POST /presets. Only what is cheap and certain is checked here: the form, the size,
 * the extension and the first bytes. Validate reads the rest with the app's own
 * readers, and until it has, the preset is listed nowhere.
 */
export async function submitPreset(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  const turnedAway = await limited(env, request, "submit");
  if (turnedAway) return turnedAway;

  const form = await formOf(request, PRESET_LIMIT, "Send the preset as a form with a file field.");
  if (form instanceof Response) return form;

  const file = form.get("file");
  if (!(file instanceof File) || file.size === 0) return badRequest("There is no file in the form.");
  if (file.size > PRESET_LIMIT) return refused(413, "That preset is too large.");

  const fileName = baseName(file.name);
  const bytes = new Uint8Array(await file.arrayBuffer());

  if (!looksLikePatch(fileName, bytes)) return badRequest("That is not a Flyback patch. Send a .fbk or .fbkb file.");

  const given = form.get("name");
  const name = typeof given === "string" && given.trim() ? given.slice(0, 1000) : stem(fileName);
  const id = newId();

  await env.FILES.put(presetFile(id), bytes);
  await env.DB.prepare(
    `INSERT INTO presets (id, name, file_name, size, submitted_at, status, search)
     VALUES (?, ?, ?, ?, ?, 'unchecked', '')`,
  )
    .bind(id, name, fileName, bytes.length, now())
    .run();

  ctx.waitUntil(dispatchValidate(env));

  const row = (await findPreset(env, id))!;
  return json(presetView(row, NO_RATING), 202, { Location: `/api/v1/presets/${id}` });
}

/** The form, or the answer that refuses it: not a form, or larger than limit and a megabyte for the rest. */
export async function formOf(request: Request, limit: number, notForm: string): Promise<FormData | Response> {
  const type = request.headers.get("Content-Type") ?? "";
  if (!/^(multipart\/form-data|application\/x-www-form-urlencoded)\b/i.test(type)) return badRequest(notForm);

  const length = Number(request.headers.get("Content-Length") ?? "0");
  if (length > limit + (1 << 20)) return refused(413, "That is too large.");

  try {
    return await request.formData();
  } catch {
    return badRequest(notForm);
  }
}

/** A .fbk is JSON, an object first; a .fbkb is a zip. */
function looksLikePatch(fileName: string, bytes: Uint8Array): boolean {
  switch (extension(fileName)) {
    case ".fbkb":
      return bytes[0] === 0x50 && bytes[1] === 0x4b && bytes[2] === 0x03 && bytes[3] === 0x04;
    case ".fbk": {
      let at = bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf ? 3 : 0;
      while (at < bytes.length && [0x20, 0x09, 0x0a, 0x0d].includes(bytes[at]!)) at++;
      return bytes[at] === 0x7b;
    }
    default:
      return false;
  }
}

/** PATCH /admin/presets/{id}: a new name, or published or not. */
export async function changePreset(env: Env, request: Request, id: string): Promise<Response> {
  const change = await body(request);
  if (change === null) return badRequest("Send the change as JSON.");

  const name = field(change, "name");
  const published = field(change, "published");

  if (typeof name === "string") {
    const called = named(name);
    if (called === null) return badRequest("A preset needs a name.");

    const row = await findPreset(env, id);
    if (row === null) return notFound();

    await env.DB.prepare("UPDATE presets SET name = ?, search = ? WHERE id = ?")
      .bind(called, folded(called, row.author, row.description), id)
      .run();
  }

  if (typeof published === "boolean") {
    const { meta } = await env.DB.prepare("UPDATE presets SET published = ? WHERE id = ?")
      .bind(published ? 1 : 0, id)
      .run();
    if (meta.changes === 0) return notFound();
  }

  const row = await findPreset(env, id);
  return row === null ? notFound() : json(presetView(row, await ratingOf(env, "preset", id)));
}

/** DELETE /admin/presets/{id}: the preset, its file, its media, its reports and its ratings. */
export async function deletePreset(env: Env, id: string): Promise<Response> {
  if (!(await forgetPreset(env, id))) return notFound();
  return noContent();
}

export async function forgetPreset(env: Env, id: string): Promise<boolean> {
  const { meta } = await env.DB.prepare("DELETE FROM presets WHERE id = ?").bind(id).run();
  if (meta.changes === 0) return false;

  await env.FILES.delete([presetFile(id), ...MEDIA_FILES.map((suffix) => mediaFile(id, suffix))]);
  await forgetReports(env, "preset", id);
  await forgetRatings(env, "preset", id);

  return true;
}

/** What Validate or the defaults say a preset file is. */
export interface CheckedPreset {
  name: string;
  author: string | null;
  description: string | null;
  tags: string[];
  lacks: unknown;
}

/** Writes what a check said of the preset into its row, and replaces its tags. */
export function describePreset(env: Env, id: string, checked: CheckedPreset): D1PreparedStatement[] {
  return [
    env.DB.prepare(
      `UPDATE presets SET name = ?, author = ?, description = ?, search = ?, lacks = ?, status = 'checked', reason = NULL
       WHERE id = ?`,
    ).bind(
      checked.name,
      checked.author,
      checked.description,
      folded(checked.name, checked.author, checked.description),
      checked.lacks == null ? null : JSON.stringify(checked.lacks),
      id,
    ),
    env.DB.prepare("DELETE FROM preset_tags WHERE preset_id = ?").bind(id),
    ...checked.tags.map((tag) =>
      env.DB.prepare("INSERT OR IGNORE INTO preset_tags (preset_id, tag) VALUES (?, ?)").bind(id, tag),
    ),
  ];
}
