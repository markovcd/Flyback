import { pluginChecked, presetChecked } from "./checks";
import type { Env } from "./env";
import { badRequest, json } from "./http";
import { pluginFile, presetFile } from "./keys";
import { describePlugin, forgetPlugin, PACKAGE_LIMIT } from "./plugins";
import { describePreset, formOf, PRESET_LIMIT } from "./presets";
import { extension, newId, now, sha256 } from "./text";

/**
 * PUT /admin/defaults/{fileName}: a preset or plugin the site starts with, sent by the
 * Site workflow with what flyback-site said of it (ADR-0138, ADR-0141). The same file
 * again changes nothing, a changed one replaces the stored file and what it says about
 * itself under the same id, and one the admin deleted stays deleted.
 */
export async function putDefault(env: Env, request: Request, fileName: string): Promise<Response> {
  if (!/^[^/\\]{1,200}\.(fbk|fbkb|fbkp)$/i.test(fileName)) return badRequest("A default is a .fbk, .fbkb or .fbkp file.");

  const plugin = extension(fileName) === ".fbkp";
  const form = await formOf(request, plugin ? PACKAGE_LIMIT : PRESET_LIMIT, "Send the default as a form with file and check fields.");
  if (form instanceof Response) return form;

  const file = form.get("file");
  const said = form.get("check");
  if (!(file instanceof File) || file.size === 0 || typeof said !== "string") return badRequest("Send the file and what the check said of it.");

  let check: Record<string, unknown>;
  try {
    check = JSON.parse(said) as Record<string, unknown>;
  } catch {
    return badRequest("The check is not JSON.");
  }

  if (check?.accepted !== true) return badRequest(`The default ${fileName} was refused: ${String(check?.reason ?? "no reason given")}`);

  const bytes = new Uint8Array(await file.arrayBuffer());
  const hash = await sha256(bytes);

  return plugin ? pluginDefault(env, fileName, bytes, hash, check) : presetDefault(env, fileName, bytes, hash, check);
}

async function presetDefault(env: Env, fileName: string, bytes: Uint8Array, hash: string, check: Record<string, unknown>) {
  const checked = presetChecked(check);
  if (typeof checked === "string") return badRequest(checked);

  const seeded = await env.DB.prepare("SELECT preset_id, hash FROM defaults WHERE file_name = ?")
    .bind(fileName)
    .first<{ preset_id: string; hash: string }>();

  if (seeded?.hash === hash) return json({ id: seeded.preset_id, state: "unchanged" });

  const id = seeded?.preset_id ?? newId();
  let state: string;

  if (seeded === null) {
    await env.DB.prepare(
      "INSERT INTO presets (id, name, file_name, size, submitted_at, status, published) VALUES (?, ?, ?, ?, ?, 'checked', 1)",
    )
      .bind(id, checked.name, fileName, bytes.length, now())
      .run();
    state = "added";
  } else {
    const { meta } = await env.DB.prepare("UPDATE presets SET size = ? WHERE id = ?").bind(bytes.length, id).run();
    state = meta.changes > 0 ? "replaced" : "deleted";
  }

  if (state !== "deleted") {
    await env.FILES.put(presetFile(id), bytes);

    // A replaced default keeps the name it was listed under, as the .NET site kept it.
    const name = seeded === null ? checked.name : ((await env.DB.prepare("SELECT name FROM presets WHERE id = ?").bind(id).first<string>("name")) ?? checked.name);
    await env.DB.batch(describePreset(env, id, { ...checked, name }));
  }

  await env.DB.prepare(
    `INSERT INTO defaults (file_name, preset_id, hash) VALUES (?, ?, ?)
     ON CONFLICT (file_name) DO UPDATE SET hash = excluded.hash`,
  )
    .bind(fileName, id, hash)
    .run();

  return json({ id, state });
}

async function pluginDefault(env: Env, fileName: string, bytes: Uint8Array, hash: string, check: Record<string, unknown>) {
  const checked = pluginChecked(check);
  if (typeof checked === "string") return badRequest(checked);

  const seeded = await env.DB.prepare("SELECT plugin_id, hash FROM plugin_defaults WHERE file_name = ?")
    .bind(fileName)
    .first<{ plugin_id: string; hash: string }>();

  if (seeded?.hash === hash) return json({ id: seeded.plugin_id, state: "unchanged" });

  const id = seeded?.plugin_id ?? newId();

  // Somebody may already have submitted this very package; the default is the copy kept.
  const { results: clashing } = await env.DB.prepare("SELECT id FROM plugins WHERE sha256 = ? AND id <> ?")
    .bind(hash, id)
    .all<{ id: string }>();
  for (const { id: other } of clashing) await forgetPlugin(env, other);

  let state: string;

  if (seeded === null) {
    await env.DB.prepare(
      `INSERT INTO plugins (id, sha256, file_name, size, submitted_at, published, status) VALUES (?, ?, ?, ?, ?, 1, 'checked')`,
    )
      .bind(id, hash, fileName, bytes.length, now())
      .run();
    state = "added";
  } else {
    const { meta } = await env.DB.prepare("UPDATE plugins SET sha256 = ?, size = ? WHERE id = ?").bind(hash, bytes.length, id).run();
    state = meta.changes > 0 ? "replaced" : "deleted";
  }

  if (state !== "deleted") {
    await env.FILES.put(pluginFile(id), bytes);
    await (await describePlugin(env, id, checked)).run();
  }

  await env.DB.prepare(
    `INSERT INTO plugin_defaults (file_name, plugin_id, hash) VALUES (?, ?, ?)
     ON CONFLICT (file_name) DO UPDATE SET hash = excluded.hash`,
  )
    .bind(fileName, id, hash)
    .run();

  return json({ id, state });
}
