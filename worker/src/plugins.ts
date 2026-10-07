import { dispatchValidate } from "./dispatch";
import type { Env } from "./env";
import { badRequest, body, field, flag, json, noContent, notFound, refused, whole } from "./http";
import { pluginFile, pluginPreview } from "./keys";
import { limited } from "./limits";
import { download, formOf } from "./presets";
import { forgetRatings, NO_RATING, type Rating, ratingOf, ratingsOf } from "./ratings";
import { forgetReports } from "./reports";
import { anywhere, baseName, extension, folded, joined, newId, now, PAIR, sha256, split, words } from "./text";

/** Half what the editor accepts, as the .NET site held a package to. */
export const PACKAGE_LIMIT = 64 * 1024 * 1024;

const PAGE_SIZE = 24;
const PLATFORMS = ["win", "osx", "linux"];

export interface PluginRow {
  id: string;
  assembly: string;
  name: string;
  version: string;
  author: string;
  description: string;
  adds: string;
  reaches: string;
  builds: string;
  contract: string;
  sha256: string;
  file_name: string;
  size: number;
  submitted_at: string;
  downloads: number;
  published: number;
  tags: string;
  preview_type: string | null;
  modules: string;
  signer: string | null;
  signer_fingerprint: string | null;
  status: "unchecked" | "checked" | "refused";
  reason: string | null;
  versions: number;
}

/** A package arrives unpublished and is seen by nobody but the admin until it is published. */
const visible = (admin: boolean): string => (admin ? "p.status = 'checked'" : "p.status = 'checked' AND p.published = 1");

/** How many published versions the plugin's assembly has, itself included. */
const VERSIONS = `(SELECT count(*) FROM plugins v WHERE v.published = 1 AND v.status = 'checked'
  AND v.assembly = p.assembly COLLATE NOCASE) AS versions`;

/** Unless a newer published package of the same assembly exists: the shelf lists a plugin once. */
const NEWEST = `NOT EXISTS (SELECT 1 FROM plugins n WHERE n.published = 1 AND n.status = 'checked'
  AND n.assembly = p.assembly COLLATE NOCASE
  AND (n.submitted_at > p.submitted_at OR (n.submitted_at = p.submitted_at AND n.id > p.id)))`;

export function pluginView(row: PluginRow, rating: Rating) {
  return {
    id: row.id,
    assembly: row.assembly,
    name: row.name,
    version: row.version,
    author: row.author,
    description: row.description,
    tags: split(row.tags),
    adds: split(row.adds),
    reaches: split(row.reaches),
    builds: split(row.builds),
    contract: Object.fromEntries(
      split(row.contract).map((c) => {
        const space = c.indexOf(" ");
        return space < 0 ? [c, ""] : [c.slice(0, space), c.slice(space + 1)];
      }),
    ),
    modules: split(row.modules).map((m) => {
      const [id, name] = m.split(PAIR, 2);
      return { id: id!, name: name ?? id! };
    }),
    sha256: row.sha256,
    signer: row.signer_fingerprint,
    fileName: row.file_name,
    size: row.size,
    submitted: row.submitted_at,
    downloads: row.downloads,
    versions: row.versions,
    published: row.published !== 0,
    file: `/api/v1/plugins/${row.id}/file`,
    preview: row.preview_type === null ? null : `/api/v1/plugins/${row.id}/preview`,
    rating: { average: rating.average, count: rating.count },
    status: row.status,
    reason: row.reason,
  };
}

export const findPlugin = (env: Env, id: string, where = "1 = 1"): Promise<PluginRow | null> =>
  env.DB.prepare(`SELECT p.*, ${VERSIONS} FROM plugins p WHERE p.id = ? AND ${where}`).bind(id).first<PluginRow>();

export const pluginShown = async (env: Env, id: string): Promise<boolean> =>
  (await findPlugin(env, id, visible(false))) !== null;

/**
 * GET /plugins: newest first, matching all of q's words, tagged tag, with a build that
 * installs on platform, and declaring module's type id, each where given. A visitor sees
 * each assembly once, at its newest published package; assembly lists every version of one.
 */
export async function listPlugins(env: Env, url: URL, admin: boolean): Promise<Response> {
  const page = whole(url, "page");
  if (page === "bad") return badRequest("A page is a whole number.");

  const platform = url.searchParams.get("platform");
  if (platform && !PLATFORMS.includes(platform)) return badRequest(`A platform is one of ${PLATFORMS.join(", ")}.`);

  const at = Math.max(1, page ?? 1);
  const where = [visible(admin)];
  const values: unknown[] = [];

  const assembly = url.searchParams.get("assembly")?.trim();
  if (assembly) {
    where.push("p.assembly = ? COLLATE NOCASE");
    values.push(assembly);
  } else if (!admin) where.push(NEWEST);

  for (const word of words(url.searchParams.get("q"))) {
    where.push("p.search LIKE ? ESCAPE '\\'");
    values.push(anywhere(word));
  }

  const tag = url.searchParams.get("tag")?.trim();
  if (tag) {
    where.push("instr(char(31) || p.tags || char(31), char(31) || ? || char(31)) > 0");
    values.push(tag.toLowerCase());
  }

  const module = url.searchParams.get("module");
  if (module) {
    where.push("instr(char(31) || p.modules, char(31) || ? || char(30)) > 0");
    values.push(module);
  }

  if (platform) {
    where.push(
      "(instr(char(31) || p.builds || char(31), char(31) || ? || char(31)) > 0 OR instr(char(31) || p.builds || char(31), char(31) || 'any' || char(31)) > 0)",
    );
    values.push(platform);
  }

  const filter = "WHERE " + where.join(" AND ");
  const [counted, listed] = await env.DB.batch<{ n: number } | PluginRow>([
    env.DB.prepare(`SELECT count(*) AS n FROM plugins p ${filter}`).bind(...values),
    env.DB.prepare(`SELECT p.*, ${VERSIONS} FROM plugins p ${filter} ORDER BY p.submitted_at DESC, p.id DESC LIMIT ? OFFSET ?`).bind(
      ...values,
      PAGE_SIZE,
      (at - 1) * PAGE_SIZE,
    ),
  ]);

  const rows = listed!.results as PluginRow[];
  const rated = await ratingsOf(env, "plugin", rows.map((r) => r.id));

  return json({
    items: rows.map((r) => pluginView(r, rated.get(r.id) ?? NO_RATING)),
    total: (counted!.results[0] as { n: number }).n,
    page: at,
    pageSize: PAGE_SIZE,
  });
}

/** GET /plugins/{id}; one still being checked, or refused, is answered to whoever has its id. */
export async function getPlugin(env: Env, id: string, admin: boolean): Promise<Response> {
  const row = await findPlugin(env, id);
  if (row === null || (row.status === "checked" && row.published === 0 && !admin)) return notFound();
  return json(pluginView(row, await ratingOf(env, "plugin", id)));
}

export async function pluginFileOf(env: Env, url: URL, id: string, admin: boolean): Promise<Response> {
  const count = flag(url, "count");
  if (count === "bad") return badRequest("count is true or false.");

  const row = await findPlugin(env, id, visible(admin));
  if (row === null) return notFound();

  const file = await env.FILES.get(pluginFile(id));
  if (file === null) return notFound();

  if (count !== false) await env.DB.prepare("UPDATE plugins SET downloads = downloads + 1 WHERE id = ?").bind(id).run();

  return download(file, row.file_name);
}

export async function pluginPreviewOf(env: Env, id: string, admin: boolean): Promise<Response> {
  const row = await findPlugin(env, id, visible(admin) + " AND p.preview_type IS NOT NULL");
  if (row === null) return notFound();

  const preview = await env.FILES.get(pluginPreview(id));
  if (preview === null) return notFound();

  return new Response(preview.body, {
    headers: {
      "Content-Type": row.preview_type!,
      "X-Content-Type-Options": "nosniff",
      "Cache-Control": "public, max-age=86400",
    },
  });
}

/** POST /plugins. The package waits unchecked for Validate, and then unpublished for the admin. */
export async function submitPlugin(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  const turnedAway = await limited(env, request, "submit");
  if (turnedAway) return turnedAway;

  const form = await formOf(request, PACKAGE_LIMIT, "Send the plugin as a form with a file field.");
  if (form instanceof Response) return form;

  const file = form.get("file");
  if (!(file instanceof File) || file.size === 0) return badRequest("There is no file in the form.");
  if (file.size > PACKAGE_LIMIT) return refused(413, "That package is too large.");

  const fileName = baseName(file.name);
  const bytes = new Uint8Array(await file.arrayBuffer());

  if (extension(fileName) !== ".fbkp" || !(bytes[0] === 0x50 && bytes[1] === 0x4b && bytes[2] === 0x03 && bytes[3] === 0x04))
    return badRequest("That is not a plugin package. Send a .fbkp file.");

  const hash = await sha256(bytes);
  const sent = await env.DB.prepare("SELECT 1 FROM plugins WHERE sha256 = ? AND status <> 'refused'").bind(hash).first();
  if (sent !== null) return refused(409, "That package has been sent already.");

  const id = newId();

  await env.FILES.put(pluginFile(id), bytes);
  await env.DB.prepare(
    `INSERT INTO plugins (id, sha256, file_name, size, submitted_at, published, status) VALUES (?, ?, ?, ?, ?, 0, 'unchecked')`,
  )
    .bind(id, hash, fileName, bytes.length, now())
    .run();

  ctx.waitUntil(dispatchValidate(env));

  const row = (await findPlugin(env, id))!;
  return json(pluginView(row, NO_RATING), 202, { Location: `/api/v1/plugins/${id}` });
}

/**
 * Whether a published plugin has assembly's name and was signed by anybody but signer:
 * the name is that plugin's. The plugin except is not counted.
 */
export async function takenByAnother(env: Env, assembly: string, signer: string | null, except: string): Promise<boolean> {
  const found = await env.DB.prepare(
    `SELECT 1 FROM plugins WHERE published = 1 AND status = 'checked' AND assembly = ? COLLATE NOCASE
     AND (signer IS NULL OR signer <> ?) AND id <> ?`,
  )
    .bind(assembly, signer ?? "", except)
    .first();

  return found !== null;
}

export const takenMessage = (assembly: string): string =>
  `A published plugin is already called ${assembly}, and was signed with another key.`;

/** PATCH /admin/plugins/{id}: published or not. */
export async function changePlugin(env: Env, request: Request, id: string): Promise<Response> {
  const change = await body(request);
  if (change === null) return badRequest("Send the change as JSON.");

  const published = field(change, "published");

  if (typeof published === "boolean") {
    const row = await findPlugin(env, id, "p.status = 'checked'");
    if (row === null) return notFound();

    if (published && (await takenByAnother(env, row.assembly, row.signer, id))) return refused(409, takenMessage(row.assembly));

    await env.DB.prepare("UPDATE plugins SET published = ? WHERE id = ?").bind(published ? 1 : 0, id).run();
  }

  const row = await findPlugin(env, id);
  return row === null ? notFound() : json(pluginView(row, await ratingOf(env, "plugin", id)));
}

export async function deletePlugin(env: Env, id: string): Promise<Response> {
  if (!(await forgetPlugin(env, id))) return notFound();
  return noContent();
}

export async function forgetPlugin(env: Env, id: string): Promise<boolean> {
  const { meta } = await env.DB.prepare("DELETE FROM plugins WHERE id = ?").bind(id).run();
  if (meta.changes === 0) return false;

  await env.FILES.delete([pluginFile(id), pluginPreview(id)]);
  await forgetReports(env, "plugin", id);
  await forgetRatings(env, "plugin", id);

  return true;
}

/** What Validate or the defaults say a package is. */
export interface CheckedPlugin {
  assembly: string;
  name: string;
  version: string;
  author: string;
  description: string;
  tags: string[];
  adds: string[];
  reaches: string[];
  builds: string[];
  contract: Record<string, string>;
  modules: { id: string; name: string }[];
  signer: string | null;
  signerFingerprint: string | null;
  preview: { type: string; data: string } | null;
}

/** Writes what a check said of the package into its row, and its preview into R2. */
export async function describePlugin(env: Env, id: string, checked: CheckedPlugin): Promise<D1PreparedStatement> {
  if (checked.preview !== null) {
    const bytes = Uint8Array.from(atob(checked.preview.data), (c) => c.charCodeAt(0));
    await env.FILES.put(pluginPreview(id), bytes, { httpMetadata: { contentType: checked.preview.type } });
  } else {
    await env.FILES.delete(pluginPreview(id));
  }

  const modules = joined(checked.modules.map((m) => m.id + PAIR + m.name));
  const tags = joined(checked.tags);

  return env.DB.prepare(
    `UPDATE plugins SET assembly = ?, name = ?, version = ?, author = ?, description = ?, tags = ?, adds = ?,
       reaches = ?, builds = ?, contract = ?, modules = ?, signer = ?, signer_fingerprint = ?, preview_type = ?,
       file_name = ?, search = ?, status = 'checked', reason = NULL
     WHERE id = ?`,
  ).bind(
    checked.assembly,
    checked.name,
    checked.version,
    checked.author,
    checked.description,
    tags,
    joined(checked.adds),
    joined(checked.reaches),
    joined(checked.builds),
    joined(Object.entries(checked.contract).map(([name, version]) => name + " " + version)),
    modules,
    checked.signer,
    checked.signerFingerprint,
    checked.preview?.type ?? null,
    checked.assembly + ".fbkp",
    folded(checked.name, checked.author, checked.description, checked.assembly, tags, modules),
    id,
  );
}

