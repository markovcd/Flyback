import type { Env } from "./env";
import { body, json, noContent, notFound, refused, text } from "./http";
import { limited } from "./limits";
import type { Kind } from "./ratings";
import { newId, now } from "./text";

/** Why a report may be made, as the site and the editor offer them. */
const REASONS = ["broken", "harmful", "offensive", "stolen", "other"];

const DETAILS_LIMIT = 1000;

/** POST of a report about what exists says is there to see. */
export async function report(
  env: Env,
  request: Request,
  kind: Kind,
  id: string,
  exists: () => Promise<boolean>,
): Promise<Response> {
  const turnedAway = await limited(env, request, "report");
  if (turnedAway) return turnedAway;

  const form = await body(request);
  if (form === null) return refused(400, "Send the report as JSON.");

  const reason = text(form, "reason");
  if (reason === null || !REASONS.includes(reason)) return refused(400, `A reason is one of ${REASONS.join(", ")}.`);

  const details = text(form, "details")?.trim() ?? null;
  if (details !== null && details.length > DETAILS_LIMIT)
    return refused(400, `Say it in ${DETAILS_LIMIT} characters or fewer.`);

  if (!(await exists())) return notFound();

  await env.DB.prepare(
    "INSERT INTO reports (id, kind, subject_id, reason, details, submitted_at) VALUES (?, ?, ?, ?, ?, ?)",
  )
    .bind(newId(), kind, id, reason, details || null, now())
    .run();

  return noContent();
}

/** Every report, newest first, with the name of what it is about now, or null where it is gone. */
export async function listReports(env: Env): Promise<Response> {
  const { results } = await env.DB.prepare(
    `SELECT r.id, r.kind, r.subject_id AS subject,
       CASE r.kind
         WHEN 'preset' THEN (SELECT p.name FROM presets p WHERE p.id = r.subject_id)
         ELSE (SELECT p.name FROM plugins p WHERE p.id = r.subject_id)
       END AS name,
       r.reason, r.details, r.submitted_at AS submitted
     FROM reports r ORDER BY r.submitted_at DESC, r.id DESC`,
  ).all();

  return json(results);
}

export async function dismissReport(env: Env, id: string): Promise<Response> {
  const { meta } = await env.DB.prepare("DELETE FROM reports WHERE id = ?").bind(id).run();
  return meta.changes > 0 ? noContent() : notFound();
}

export async function forgetReports(env: Env, kind: Kind, id: string): Promise<void> {
  await env.DB.prepare("DELETE FROM reports WHERE kind = ? AND subject_id = ?").bind(kind, id).run();
}
