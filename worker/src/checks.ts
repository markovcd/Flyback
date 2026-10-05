import type { Env } from "./env";
import { badRequest, body, field, json, notFound, refused } from "./http";
import { pluginFile, presetFile } from "./keys";
import {
  type CheckedPlugin,
  describePlugin,
  findPlugin,
  pluginView,
  takenByAnother,
  takenMessage,
} from "./plugins";
import { type CheckedPreset, describePreset, download, findPreset, presetView } from "./presets";
import { NO_RATING } from "./ratings";
import { now } from "./text";

/**
 * The Validate workflow's side of the admin API: what waits to be checked, the files
 * themselves, and what flyback-site check-submission said of each. The Worker never
 * reads a patch or a package; the app's own readers do, on GitHub's machine.
 */

const BATCH = 50;
const REASON_LIMIT = 2000;
const WEEK_MS = 7 * 24 * 3600 * 1000;

/** GET /admin/unchecked: the oldest submissions still waiting, of each kind. */
export async function listUnchecked(env: Env): Promise<Response> {
  const [presets, plugins] = await env.DB.batch([
    env.DB.prepare(
      "SELECT id, name, file_name AS fileName FROM presets WHERE status = 'unchecked' ORDER BY submitted_at, id LIMIT ?",
    ).bind(BATCH),
    env.DB.prepare(
      "SELECT id, file_name AS fileName FROM plugins WHERE status = 'unchecked' ORDER BY submitted_at, id LIMIT ?",
    ).bind(BATCH),
  ]);

  return json({ presets: presets!.results, plugins: plugins!.results });
}

/** GET /admin/presets: every checked preset's id and file name, for the pass that refreshes what browsers lack. */
export async function listCheckedPresets(env: Env): Promise<Response> {
  const { results } = await env.DB.prepare(
    "SELECT id, file_name AS fileName FROM presets WHERE status = 'checked' ORDER BY submitted_at, id",
  ).all();

  return json({ items: results });
}

/** GET /admin/{presets|plugins}/{id}/file: the file whatever its state, never counted. */
export async function anyFile(env: Env, kind: "preset" | "plugin", id: string): Promise<Response> {
  const row = kind === "preset" ? await findPreset(env, id) : await findPlugin(env, id);
  if (row === null) return notFound();

  const file = await env.FILES.get(kind === "preset" ? presetFile(id) : pluginFile(id));
  return file === null ? notFound() : download(file, row.file_name);
}

/** PUT /admin/presets/{id}/check. */
export async function checkPreset(env: Env, request: Request, id: string): Promise<Response> {
  const result = await body(request);
  if (result === null) return badRequest("Send what the check said as JSON.");

  const row = await findPreset(env, id);
  if (row === null) return notFound();
  if (row.status !== "unchecked") return refused(409, "That preset has been checked already.");

  const verdict = refusal(result);
  if (verdict !== undefined) {
    if (verdict === null) return badRequest("A refusal says why.");
    await refuse(env, "presets", id, verdict);
  } else {
    const checked = presetChecked(result);
    if (typeof checked === "string") return badRequest(checked);
    await env.DB.batch(describePreset(env, id, checked));
  }

  return json(presetView((await findPreset(env, id))!, NO_RATING));
}

/** PUT /admin/plugins/{id}/check. An accepted package is still unpublished until the admin publishes it. */
export async function checkPlugin(env: Env, request: Request, id: string): Promise<Response> {
  const result = await body(request);
  if (result === null) return badRequest("Send what the check said as JSON.");

  const row = await findPlugin(env, id);
  if (row === null) return notFound();
  if (row.status !== "unchecked") return refused(409, "That package has been checked already.");

  const verdict = refusal(result);
  if (verdict !== undefined) {
    if (verdict === null) return badRequest("A refusal says why.");
    await refuse(env, "plugins", id, verdict);
  } else {
    const checked = pluginChecked(result);
    if (typeof checked === "string") return badRequest(checked);

    if (await takenByAnother(env, checked.assembly, checked.signer, id)) await refuse(env, "plugins", id, takenMessage(checked.assembly));
    else await (await describePlugin(env, id, checked)).run();
  }

  return json(pluginView((await findPlugin(env, id))!, NO_RATING));
}

/** PUT /admin/presets/{id}/lacks: what the web pages of this build lack to open the preset, or null. */
export async function putLacks(env: Env, request: Request, id: string): Promise<Response> {
  let lacks: unknown;
  try {
    lacks = await request.json();
  } catch {
    return badRequest("Send what the pages lack as JSON, or null.");
  }

  const checked = lacks === null ? null : lacksOf(lacks);
  if (typeof checked === "string") return badRequest(checked);

  const { meta } = await env.DB.prepare("UPDATE presets SET lacks = ? WHERE id = ? AND status = 'checked'")
    .bind(checked === null ? null : JSON.stringify(checked), id)
    .run();

  return meta.changes > 0 ? new Response(null, { status: 204 }) : notFound();
}

/** Forgets refused submissions a week after they were sent, files and all. */
export async function forgetRefused(env: Env): Promise<void> {
  const before = now(new Date(Date.now() - WEEK_MS));

  for (const [table, key] of [["presets", presetFile], ["plugins", pluginFile]] as const) {
    const { results } = await env.DB.prepare(`SELECT id FROM ${table} WHERE status = 'refused' AND submitted_at < ?`)
      .bind(before)
      .all<{ id: string }>();

    for (const { id } of results) {
      await env.FILES.delete(key(id));
      await env.DB.prepare(`DELETE FROM ${table} WHERE id = ?`).bind(id).run();
    }
  }
}

async function refuse(env: Env, table: "presets" | "plugins", id: string, reason: string): Promise<void> {
  await env.DB.prepare(`UPDATE ${table} SET status = 'refused', reason = ? WHERE id = ?`)
    .bind(reason.slice(0, REASON_LIMIT), id)
    .run();
}

/** undefined where the check accepted the file, the reason where it refused it, null where it refused without one. */
function refusal(result: Record<string, unknown>): string | null | undefined {
  if (field(result, "accepted") === true) return undefined;
  const reason = field(result, "reason");
  return typeof reason === "string" && reason.trim() ? reason.trim() : null;
}

const SHORT = 400;

function string(from: Record<string, unknown>, name: string, nullable: true): string | null | Error;
function string(from: Record<string, unknown>, name: string, nullable?: false): string | Error;
function string(from: Record<string, unknown>, name: string, nullable = false): string | null | Error {
  const value = field(from, name);
  if (value == null && nullable) return null;
  return typeof value === "string" && value.length <= SHORT * 10 ? value : new Error(`${name} is not text.`);
}

function strings(from: Record<string, unknown>, name: string): string[] | Error {
  const value = field(from, name) ?? [];
  return Array.isArray(value) && value.length <= 1000 && value.every((v) => typeof v === "string" && v.length <= SHORT)
    ? (value as string[])
    : new Error(`${name} is not a list of text.`);
}

function firstError(...values: unknown[]): string | null {
  for (const value of values) if (value instanceof Error) return value.message;
  return null;
}

function presetChecked(result: Record<string, unknown>): CheckedPreset | string {
  const name = string(result, "name");
  const author = string(result, "author", true);
  const description = string(result, "description", true);
  const tags = strings(result, "tags");
  const raw = field(result, "lacks");
  const lacks = raw == null ? null : lacksOf(raw);

  const wrong = firstError(name, author, description, tags) ?? (typeof lacks === "string" ? lacks : null);
  if (wrong !== null) return wrong;
  if (!(name as string).trim()) return "A preset needs a name.";

  return {
    name: name as string,
    author: author as string | null,
    description: description as string | null,
    tags: tags as string[],
    lacks,
  };
}

/** What a browser lacks, as BrowserLack serializes: the plugins it names, how many modules, and the line said. */
function lacksOf(value: unknown): { plugins: { id: string; name: string }[]; modules: number; said: string } | string {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return "lacks is not an object.";
  const from = value as Record<string, unknown>;

  const plugins = field(from, "plugins") ?? [];
  const modules = field(from, "modules");
  const said = field(from, "said");

  if (
    !Array.isArray(plugins) ||
    plugins.length > 100 ||
    !plugins.every((p) => p !== null && typeof p === "object" && typeof field(p, "id") === "string" && typeof field(p, "name") === "string")
  )
    return "lacks.plugins is not a list of plugins.";
  if (typeof modules !== "number" || !Number.isInteger(modules) || modules < 0) return "lacks.modules is not a count.";
  if (typeof said !== "string" || said.length > SHORT) return "lacks.said is not text.";

  return {
    plugins: plugins.map((p: Record<string, unknown>) => ({ id: field(p, "id") as string, name: field(p, "name") as string })),
    modules,
    said,
  };
}

function pluginChecked(result: Record<string, unknown>): CheckedPlugin | string {
  const values = {
    assembly: string(result, "assembly"),
    name: string(result, "name"),
    version: string(result, "version"),
    author: string(result, "author"),
    description: string(result, "description"),
    tags: strings(result, "tags"),
    adds: strings(result, "adds"),
    reaches: strings(result, "reaches"),
    builds: strings(result, "builds"),
    signer: string(result, "signer", true),
    signerFingerprint: string(result, "signerFingerprint", true),
  };

  const wrong = firstError(...Object.values(values));
  if (wrong !== null) return wrong;
  if (!/^[A-Za-z0-9_.-]{1,200}$/.test(values.assembly as string)) return "assembly is not an assembly's name.";

  const contract = field(result, "contract") ?? {};
  if (
    contract === null ||
    typeof contract !== "object" ||
    Array.isArray(contract) ||
    !Object.entries(contract).every(([k, v]) => typeof v === "string" && k.length <= SHORT && !k.includes(" "))
  )
    return "contract is not a map of assembly to version.";

  const modules = field(result, "modules") ?? [];
  if (
    !Array.isArray(modules) ||
    !modules.every((m) => m !== null && typeof m === "object" && typeof field(m, "id") === "string" && typeof field(m, "name") === "string")
  )
    return "modules is not a list of modules.";

  const preview = field(result, "preview");
  let kept: { type: string; data: string } | null = null;

  if (preview != null) {
    const type = typeof preview === "object" ? field(preview as Record<string, unknown>, "type") : null;
    const data = typeof preview === "object" ? field(preview as Record<string, unknown>, "data") : null;
    if (typeof type !== "string" || !/^image\/(png|webp)$/.test(type) || typeof data !== "string")
      return "preview is not an image.";
    kept = { type, data };
  }

  return {
    ...(values as unknown as Omit<CheckedPlugin, "contract" | "modules" | "preview">),
    contract: contract as Record<string, string>,
    modules: modules.map((m: Record<string, unknown>) => ({ id: field(m, "id") as string, name: field(m, "name") as string })),
    preview: kept,
  };
}

export { presetChecked, pluginChecked };
