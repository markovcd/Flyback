import type { Env } from "./env";
import { body, field, json, notFound, refused } from "./http";
import { limited } from "./limits";
import { hex, now } from "./text";
import { visitorOf } from "./visitor";

export type Kind = "preset" | "plugin";

export interface Rating {
  average: number;
  count: number;
}

export const NO_RATING: Rating = { average: 0, count: 0 };

const MOST = 5;

/** The rating of each of ids that has one. */
export async function ratingsOf(env: Env, kind: Kind, ids: readonly string[]): Promise<Map<string, Rating>> {
  const found = new Map<string, Rating>();
  const distinct = [...new Set(ids)];
  if (distinct.length === 0) return found;

  const { results } = await env.DB.prepare(
    `SELECT subject_id, AVG(stars) AS average, COUNT(*) AS count FROM ratings
     WHERE kind = ? AND subject_id IN (${distinct.map(() => "?").join(", ")}) GROUP BY subject_id`,
  )
    .bind(kind, ...distinct)
    .all<{ subject_id: string; average: number; count: number }>();

  for (const row of results) found.set(row.subject_id, { average: hundredths(row.average), count: row.count });

  return found;
}

export const ratingOf = async (env: Env, kind: Kind, id: string): Promise<Rating> =>
  (await ratingsOf(env, kind, [id])).get(id) ?? NO_RATING;

/** Rounded as Math.Round(value, 2) rounds: a half goes to the even neighbor. */
function hundredths(value: number): number {
  const scaled = value * 100;
  const floor = Math.floor(scaled);
  const half = Math.abs(scaled - floor - 0.5) < 1e-9;
  return (half ? (floor % 2 === 0 ? floor : floor + 1) : Math.round(scaled)) / 100;
}

/** GET of a rating: its average, its count, and what this visitor gave. */
export async function readRating(env: Env, request: Request, kind: Kind, id: string, exists: boolean): Promise<Response> {
  if (!exists) return notFound();
  return json(await said(env, kind, id, await voter(env, request)));
}

/**
 * PUT of a rating. Taken only where the browser says the request came from the site's
 * own page, which the editor never sends: the editor shows ratings and leaves giving
 * them to the site.
 */
export async function giveRating(
  env: Env,
  request: Request,
  kind: Kind,
  id: string,
  exists: () => Promise<boolean>,
): Promise<Response> {
  const turnedAway = await limited(env, request, "rate");
  if (turnedAway) return turnedAway;

  if (request.headers.get("Sec-Fetch-Site") !== "same-origin") return refused(403, "Rate it on its page on the preset site.");

  const form = await body(request);
  if (form === null) return refused(400, "Send the rating as JSON.");

  const stars = field(form, "stars");
  if (typeof stars !== "number" || !Number.isInteger(stars) || stars < 1 || stars > MOST)
    return refused(400, `A rating is 1 to ${MOST} stars.`);

  if (!(await exists())) return notFound();

  const who = await voter(env, request);

  await env.DB.prepare(
    `INSERT INTO ratings (kind, subject_id, voter, stars, rated_at) VALUES (?, ?, ?, ?, ?)
     ON CONFLICT (kind, subject_id, voter) DO UPDATE SET stars = excluded.stars, rated_at = excluded.rated_at`,
  )
    .bind(kind, id, who, stars, now())
    .run();

  return json(await said(env, kind, id, who));
}

export async function forgetRatings(env: Env, kind: Kind, id: string): Promise<void> {
  await env.DB.prepare("DELETE FROM ratings WHERE kind = ? AND subject_id = ?").bind(kind, id).run();
}

async function said(env: Env, kind: Kind, id: string, who: string) {
  const rating = await ratingOf(env, kind, id);
  const mine = await env.DB.prepare("SELECT stars FROM ratings WHERE kind = ? AND subject_id = ? AND voter = ?")
    .bind(kind, id, who)
    .first<number>("stars");

  return { average: rating.average, count: rating.count, mine: mine ?? null };
}

/** Who is rating, as the table keeps them: an HMAC of the visitor under the site's own key. */
async function voter(env: Env, request: Request): Promise<string> {
  const stored = await env.DB.prepare("SELECT key FROM rating_key LIMIT 1").first<ArrayBuffer | number[]>("key");
  if (stored === null) throw new Error("The rating key is missing; the migrations have not been applied.");

  const key = await crypto.subtle.importKey(
    "raw",
    stored instanceof ArrayBuffer ? stored : new Uint8Array(stored),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );

  return hex(new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(visitorOf(request)))));
}
