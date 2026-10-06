import type { Env } from "./env";

/** The anonymous reads whose answer is the same for every visitor: the lists and one entry, never a file, a rating or the render queue. */
const READS = /^\/api\/v1\/(presets|plugins)(\/[^/]+)?$|^\/api\/v1\/tags$/;

/** How long a read is kept, in seconds; 0 or unset keeps nothing. */
const seconds = (env: Env): number => {
  const set = Number(env.READ_CACHE_SECONDS);
  return Number.isFinite(set) && set > 0 ? Math.min(set, 3600) : 0;
};

/**
 * Answers an anonymous GET from the edge's cache where it has one, and keeps the answer
 * otherwise, so a crowd costs one database read a window rather than one each.
 * The browser keeps it for the same window.
 */
export async function readCached(
  env: Env,
  ctx: ExecutionContext,
  request: Request,
  url: URL,
  admin: boolean,
  answer: () => Promise<Response>,
): Promise<Response> {
  const ttl = seconds(env);
  const cacheable =
    ttl > 0 && !admin && request.method === "GET" && READS.test(url.pathname) && url.searchParams.get("pending") !== "true";
  if (!cacheable) return answer();

  const key = new Request(url.toString(), { method: "GET" });
  const cache = (caches as unknown as { default: Cache }).default;

  const kept = await cache.match(key);
  if (kept !== undefined) return kept;

  const response = await answer();
  if (response.status !== 200) return response;

  const stored = new Response(response.body, response);
  stored.headers.set("Cache-Control", `public, max-age=${ttl}`);
  ctx.waitUntil(cache.put(key, stored.clone()));
  return stored;
}
