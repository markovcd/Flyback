import type { Env } from "./env";
import { refused } from "./http";
import { visitorOf } from "./visitor";

/** How many requests one visitor may make of each kind in an hour, and the variable that changes it. */
const POLICIES = {
  submit: { variable: "POSTS_PER_HOUR", otherwise: 20 },
  report: { variable: "REPORTS_PER_HOUR", otherwise: 10 },
  letter: { variable: "LETTERS_PER_HOUR", otherwise: 5 },
  rate: { variable: "RATINGS_PER_HOUR", otherwise: 60 },
} as const;

export type Policy = keyof typeof POLICIES;

const WINDOW = 3600;

/** Counts the request, and answers 429 where the visitor has used the hour's allowance. */
export async function limited(env: Env, request: Request, policy: Policy): Promise<Response | null> {
  const { variable, otherwise } = POLICIES[policy];
  const set = Number(env[variable]);
  const allowance = Number.isFinite(set) && set > 0 ? set : otherwise;
  const window = Math.floor(Date.now() / 1000 / WINDOW) * WINDOW;

  const counted = await env.DB.prepare(
    `INSERT INTO limits (visitor, policy, window, count) VALUES (?, ?, ?, 1)
     ON CONFLICT (visitor, policy, window) DO UPDATE SET count = count + 1
     RETURNING count`,
  )
    .bind(visitorOf(request), policy, window)
    .first<number>("count");

  return (counted ?? 0) > allowance ? refused(429, "Too many at once. Try again in an hour.") : null;
}

/** Forgets every window that has closed. */
export async function forgetSpentWindows(env: Env): Promise<void> {
  await env.DB.prepare("DELETE FROM limits WHERE window < ?")
    .bind(Math.floor(Date.now() / 1000) - WINDOW)
    .run();
}
