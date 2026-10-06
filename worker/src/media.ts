import type { Env } from "./env";
import { badRequest, noContent, notFound, refused } from "./http";
import { mediaFile } from "./keys";
import { KEPT_FOR_GOOD } from "./large";

/**
 * What flyback-cli render-presets makes of a preset, by the name it uploads each under:
 * a still, a loop, a track and the player's bars, then done or failed with the reason.
 */
const SERVED = {
  webp: { suffix: ".webp", type: "image/webp" },
  webm: { suffix: ".webm", type: "video/webm" },
  mp3: { suffix: ".mp3", type: "audio/mpeg" },
  "peaks.json": { suffix: ".peaks.json", type: "application/json" },
} as const;

type Served = keyof typeof SERVED;

/** Every file a preset's media may leave in R2. */
export const MEDIA_FILES = Object.values(SERVED).map((s) => s.suffix);

const FILE_LIMIT = 50 * 1024 * 1024;
const REASON_LIMIT = 2000;
const PEAKS_LIMIT = 1000;

interface MediaColumns {
  id: string;
  media_state: string;
  media: string;
  media_rev: number;
  peaks: string | null;
}

/** The media part of a preset's entry: where each file is, the bars, and whether the render is done. */
export function mediaOf(row: MediaColumns) {
  const has = new Set(row.media ? row.media.split(",") : []);
  const url = (name: Served) => (has.has(name) ? `/media/${row.id}${SERVED[name].suffix}?v=${row.media_rev}` : null);

  return {
    still: url("webp"),
    loop: url("webm"),
    audio: url("mp3"),
    peaks: row.peaks ? (JSON.parse(row.peaks) as number[]) : null,
    state: row.media_state,
  };
}

/**
 * GET /media/{id}.{suffix}, with ranges for the player. A URL with the revision (?v=) is kept for good,
 * since a new render changes it; one without is checked before each use.
 */
export async function serveMedia(env: Env, request: Request, url: URL, name: string): Promise<Response> {
  const match = /^([0-9a-f]{32})(\.webp|\.webm|\.mp3|\.peaks\.json)$/.exec(name);
  if (match === null) return notFound();

  const type = Object.values(SERVED).find((s) => s.suffix === match[2])!.type;
  const object = await env.FILES.get(mediaFile(match[1]!, match[2]!), {
    range: request.headers,
    onlyIf: request.headers,
  });

  if (object === null) return notFound();

  const headers = new Headers({
    "Content-Type": type,
    "Cache-Control": url.searchParams.has("v") ? KEPT_FOR_GOOD : "no-cache",
    ETag: object.httpEtag,
    "Accept-Ranges": "bytes",
  });

  if (!("body" in object)) return new Response(null, { status: 304, headers });

  const range = object.range as { offset?: number; length?: number } | undefined;
  if (request.headers.has("Range") && range && range.offset !== undefined && range.length !== undefined) {
    headers.set("Content-Range", `bytes ${range.offset}-${range.offset + range.length - 1}/${object.size}`);
    return new Response(object.body, { status: 206, headers });
  }

  return new Response(object.body, { headers });
}

/**
 * PUT /admin/presets/{id}/media/{name}. Only the names render-presets makes are taken,
 * since the name becomes an object's key. done goes last, so a page never shows half a render.
 */
export async function putMedia(env: Env, request: Request, id: string, name: string): Promise<Response> {
  const exists = await env.DB.prepare("SELECT 1 FROM presets WHERE id = ? AND status = 'checked'").bind(id).first();
  if (exists === null) return notFound();

  if (Number(request.headers.get("Content-Length") ?? "0") > FILE_LIMIT) return refused(413, "That file is too large.");

  const bytes = new Uint8Array(await request.arrayBuffer());
  if (bytes.length > FILE_LIMIT) return refused(413, "That file is too large.");

  if (name === "done") {
    await env.DB.prepare("UPDATE presets SET media_state = 'done', media_reason = NULL WHERE id = ?").bind(id).run();
    return noContent();
  }

  if (name === "failed") {
    const why = new TextDecoder().decode(bytes).slice(0, REASON_LIMIT);
    await env.DB.prepare("UPDATE presets SET media_state = 'failed', media_reason = ? WHERE id = ?").bind(why, id).run();
    return noContent();
  }

  if (!(name in SERVED)) return badRequest("A render's files are webp, webm, mp3, peaks.json, done and failed.");

  const served = SERVED[name as Served];
  let peaks: string | null = null;

  if (name === "peaks.json") {
    try {
      const read: unknown = JSON.parse(new TextDecoder().decode(bytes));
      if (!Array.isArray(read) || read.length > PEAKS_LIMIT || !read.every((p) => typeof p === "number"))
        throw new Error();
      peaks = JSON.stringify(read);
    } catch {
      return badRequest("The bars are a JSON array of numbers.");
    }
  }

  await env.FILES.put(mediaFile(id, served.suffix), bytes, { httpMetadata: { contentType: served.type } });

  const row = await env.DB.prepare("SELECT media FROM presets WHERE id = ?").bind(id).first<{ media: string }>();
  const has = new Set(row?.media ? row.media.split(",") : []);
  has.add(name);

  await env.DB.prepare(`UPDATE presets SET media = ?, media_rev = media_rev + 1${peaks === null ? "" : ", peaks = ?"} WHERE id = ?`)
    .bind(...[[...has].sort().join(","), ...(peaks === null ? [] : [peaks]), id])
    .run();

  return noContent();
}

/** DELETE /admin/presets/{id}/media: forgets the render, which puts the preset back in the queue. */
export async function clearMedia(env: Env, id: string): Promise<Response> {
  const { meta } = await env.DB.prepare(
    "UPDATE presets SET media_state = 'pending', media = '', media_rev = media_rev + 1, media_reason = NULL, peaks = NULL WHERE id = ?",
  )
    .bind(id)
    .run();

  if (meta.changes === 0) return notFound();

  await env.FILES.delete(MEDIA_FILES.map((suffix) => mediaFile(id, suffix)));
  return noContent();
}
