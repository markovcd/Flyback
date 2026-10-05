import type { Env } from "./env";
import { badRequest, noContent, refused } from "./http";
import { largeAsset } from "./keys";

/** The fingerprinted framework files of the web viewer and editor, which never change once named. */
export const FRAMEWORK = /^\/?(viewer|editor)\/_framework\/[A-Za-z0-9._-]+\.[a-z0-9]{10}\.[a-z]+$/;

export const KEPT_FOR_GOOD = "public, max-age=31536000, immutable";

const TYPES: Record<string, string> = {
  ".wasm": "application/wasm",
  ".js": "text/javascript",
  ".json": "application/json",
};

const LIMIT = 90 * 1024 * 1024;

const typeOf = (path: string): string => TYPES[path.slice(path.lastIndexOf("."))] ?? "application/octet-stream";

/** A framework file too large to be an asset, from R2, or null where it is not there either. */
export async function largeFile(env: Env, request: Request, path: string): Promise<Response | null> {
  const object = await env.FILES.get(largeAsset(path.replace(/^\//, "")), { onlyIf: request.headers });
  if (object === null) return null;

  const headers = new Headers({ "Content-Type": typeOf(path), "Cache-Control": KEPT_FOR_GOOD, ETag: object.httpEtag });
  return "body" in object ? new Response(object.body, { headers }) : new Response(null, { status: 304, headers });
}

/** PUT /admin/assets/{path}: a framework file the Worker workflow found too large to deploy as an asset. */
export async function putLargeFile(env: Env, request: Request, path: string): Promise<Response> {
  if (!FRAMEWORK.test(path)) return badRequest("Only the viewer's and the editor's fingerprinted framework files are kept here.");
  if (Number(request.headers.get("Content-Length") ?? "0") > LIMIT) return refused(413, "That file is too large.");

  const bytes = await request.arrayBuffer();
  if (bytes.byteLength > LIMIT) return refused(413, "That file is too large.");

  await env.FILES.put(largeAsset(path), bytes, { httpMetadata: { contentType: typeOf(path) } });
  return noContent();
}
