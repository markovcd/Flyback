import { dispatchValidate } from "./dispatch";
import type { Env } from "./env";
import { badRequest, body, field, json, notFound, refused } from "./http";
import { packFile } from "./keys";
import { limited } from "./limits";
import { NO_RATING, type Rating, ratingOf } from "./ratings";
import { fileOf, find, formOf, one, page, remove, shown, type Store } from "./store";
import { baseName, extension, folded, joined, newId, now, PAIR, sha256, split } from "./text";

/** What the app reads a pack up to, InstalledPacks.MostBytes. */
export const PACK_LIMIT = 1 << 20;

/** A pack's id as its header names one: dotted lowercase words. */
export const PACK_ID = /^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$/;

const VERSION = /^\d{1,9}\.\d{1,9}\.\d{1,9}(-[0-9A-Za-z.-]{1,40})?$/;

export interface PackRow {
  id: string;
  pack: string;
  version: string;
  modules: string;
  sha256: string;
  file_name: string;
  size: number;
  submitted_at: string;
  downloads: number;
  published: number;
  status: "unchecked" | "checked" | "refused";
  reason: string | null;
  versions: number;
}

/** How many published versions the pack's id has, itself included. */
const VERSIONS = `(SELECT count(*) FROM packs v WHERE v.published = 1 AND v.status = 'checked' AND v.pack = p.pack) AS versions`;

/** Unless a newer published version of the same pack exists: the shelf lists a pack once. */
const NEWEST = `NOT EXISTS (SELECT 1 FROM packs n WHERE n.published = 1 AND n.status = 'checked' AND n.pack = p.pack
  AND (n.submitted_at > p.submitted_at OR (n.submitted_at = p.submitted_at AND n.id > p.id)))`;

export function packView(row: PackRow, rating: Rating) {
  return {
    id: row.id,
    pack: row.pack,
    version: row.version,
    modules: split(row.modules).map((m) => {
      const [id, name] = m.split(PAIR, 2);
      return { id: id!, name: name ?? id! };
    }),
    sha256: row.sha256,
    fileName: row.file_name,
    size: row.size,
    submitted: row.submitted_at,
    downloads: row.downloads,
    versions: row.versions,
    published: row.published !== 0,
    file: `/api/v1/packs/${row.id}/file`,
    rating: { average: rating.average, count: rating.count },
    status: row.status,
    reason: row.reason,
  };
}

const PACKS: Store<PackRow> = {
  kind: "pack",
  table: "packs",
  columns: `p.*, ${VERSIONS}`,
  view: packView,
  file: packFile,
  owned: (id) => [packFile(id)],
};

export const findPack = (env: Env, id: string, where?: string): Promise<PackRow | null> => find(PACKS, env, id, where);

export const packShown = (env: Env, id: string): Promise<boolean> => shown(PACKS, env, id);

/**
 * GET /packs: newest first, matching all of q's words and offering module's type id,
 * each where given. A visitor sees each pack once, at its newest published version;
 * pack lists every version of one.
 */
export async function listPacks(env: Env, url: URL, admin: boolean): Promise<Response> {
  const where: string[] = [];
  const values: unknown[] = [];

  const pack = url.searchParams.get("pack")?.trim();
  if (pack) {
    where.push("p.pack = ?");
    values.push(pack);
  } else if (!admin) where.push(NEWEST);

  const module = url.searchParams.get("module");
  if (module) {
    where.push("instr(char(31) || p.modules, char(31) || ? || char(30)) > 0");
    values.push(module);
  }

  return page(PACKS, env, url, admin, where, values);
}

/** GET /packs/{id}; one still being checked, or refused, is answered to whoever has its id. */
export const getPack = (env: Env, id: string, admin: boolean): Promise<Response> => one(PACKS, env, id, admin);

export const packFileOf = (env: Env, url: URL, id: string, admin: boolean): Promise<Response> => fileOf(PACKS, env, url, id, admin);

/** POST /packs. The text waits unchecked for Validate, and then unpublished for the admin. */
export async function submitPack(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  const turnedAway = await limited(env, request, "submit");
  if (turnedAway) return turnedAway;

  const form = await formOf(request, PACK_LIMIT, "Send the pack as a form with a file field.");
  if (form instanceof Response) return form;

  const file = form.get("file");
  if (!(file instanceof File) || file.size === 0) return badRequest("There is no file in the form.");
  if (file.size > PACK_LIMIT) return refused(413, "That pack is too large.");

  const fileName = baseName(file.name);
  const bytes = new Uint8Array(await file.arrayBuffer());

  if (extension(fileName) !== ".fbks" || !isText(bytes)) return badRequest("That is not a module pack. Send a .fbks file.");

  const hash = await sha256(bytes);
  const sent = await env.DB.prepare("SELECT 1 FROM packs WHERE sha256 = ? AND status <> 'refused'").bind(hash).first();
  if (sent !== null) return refused(409, "That pack has been sent already.");

  const id = newId();

  await env.FILES.put(packFile(id), bytes);
  await env.DB.prepare(
    `INSERT INTO packs (id, sha256, file_name, size, submitted_at, published, status) VALUES (?, ?, ?, ?, ?, 0, 'unchecked')`,
  )
    .bind(id, hash, fileName, bytes.length, now())
    .run();

  ctx.waitUntil(dispatchValidate(env));

  const row = (await findPack(env, id))!;
  return json(packView(row, NO_RATING), 202, { Location: `/api/v1/packs/${id}` });
}

function isText(bytes: Uint8Array): boolean {
  try {
    new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(bytes);
    return !bytes.includes(0);
  } catch {
    return false;
  }
}

/** PATCH /admin/packs/{id}: published or not. */
export async function changePack(env: Env, request: Request, id: string): Promise<Response> {
  const change = await body(request);
  if (change === null) return badRequest("Send the change as JSON.");

  const published = field(change, "published");

  if (typeof published === "boolean") {
    const row = await findPack(env, id, "p.status = 'checked'");
    if (row === null) return notFound();

    await env.DB.prepare("UPDATE packs SET published = ? WHERE id = ?").bind(published ? 1 : 0, id).run();
  }

  const row = await findPack(env, id);
  return row === null ? notFound() : json(packView(row, await ratingOf(env, "pack", id)));
}

export const deletePack = (env: Env, id: string): Promise<Response> => remove(PACKS, env, id);

/** What Validate says a pack is. */
export interface CheckedPack {
  pack: string;
  version: string;
  modules: { id: string; name: string }[];
}

/** What the check said of the pack, or what is wrong with what it said. */
export function packChecked(result: Record<string, unknown>): CheckedPack | string {
  const pack = field(result, "pack");
  const version = field(result, "version");
  const modules = field(result, "modules");

  if (typeof pack !== "string" || pack.length > 200 || !PACK_ID.test(pack)) return "pack is not a pack's id.";
  if (typeof version !== "string" || !VERSION.test(version)) return "version is not a version.";
  if (
    !Array.isArray(modules) ||
    modules.length === 0 ||
    modules.length > 1000 ||
    !modules.every(
      (m) =>
        m !== null &&
        typeof m === "object" &&
        typeof field(m, "id") === "string" &&
        typeof field(m, "name") === "string" &&
        (field(m, "id") as string).length <= 400 &&
        (field(m, "name") as string).length <= 400,
    )
  )
    return "modules is not a list of modules.";

  return {
    pack,
    version,
    modules: modules.map((m: Record<string, unknown>) => ({ id: field(m, "id") as string, name: field(m, "name") as string })),
  };
}

/** Writes what a check said of the pack into its row. */
export function describePack(env: Env, id: string, checked: CheckedPack): D1PreparedStatement {
  const modules = joined(checked.modules.map((m) => m.id + PAIR + m.name));

  return env.DB.prepare(
    `UPDATE packs SET pack = ?, version = ?, modules = ?, file_name = ?, search = ?, status = 'checked', reason = NULL WHERE id = ?`,
  ).bind(checked.pack, checked.version, modules, checked.pack + ".fbks", folded(checked.pack, modules), id);
}
