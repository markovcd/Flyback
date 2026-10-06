import { adminEnabled, isAdmin } from "./access";
import { anyFile, checkPlugin, checkPreset, forgetRefused, listCheckedPresets, listUnchecked, putLacks } from "./checks";
import { putDefault } from "./defaults";
import type { Env } from "./env";
import { json, notFound, unauthorized } from "./http";
import { dismissLetter, listLetters, writeLetter } from "./letters";
import { forgetSpentWindows } from "./limits";
import { clearMedia, putMedia, serveMedia } from "./media";
import {
  changePlugin,
  deletePlugin,
  getPlugin,
  listPlugins,
  pluginFileOf,
  pluginPreviewOf,
  pluginShown,
  submitPlugin,
} from "./plugins";
import {
  changePreset,
  deletePreset,
  getPreset,
  listPresets,
  listTags,
  presetFileOf,
  presetShown,
  submitPreset,
} from "./presets";
import { FRAMEWORK, KEPT_FOR_GOOD, largeFile, putLargeFile } from "./large";
import { readCached } from "./readCache";
import { giveRating, readRating } from "./ratings";
import { dismissReport, listReports, report } from "./reports";

export type { Env } from "./env";

interface Asked {
  request: Request;
  env: Env;
  ctx: ExecutionContext;
  url: URL;
  /** The route's captures, decoded. */
  at: string[];
  admin: boolean;
}

type Route = [method: string, path: RegExp, answer: (asked: Asked) => Promise<Response> | Response];

const ID = "([^/]+)";
const path = (pattern: string): RegExp => new RegExp("^" + pattern.replaceAll("{id}", ID) + "$");

/** What anyone may ask. */
const PUBLIC: Route[] = [
  ["GET", path("/api/v1/presets"), (a) => listPresets(a.env, a.url, a.admin)],
  ["POST", path("/api/v1/presets"), (a) => submitPreset(a.env, a.request, a.ctx)],
  ["GET", path("/api/v1/presets/{id}"), (a) => getPreset(a.env, a.at[0]!, a.admin)],
  ["GET", path("/api/v1/presets/{id}/file"), (a) => presetFileOf(a.env, a.url, a.at[0]!, a.admin)],
  ["GET", path("/api/v1/presets/{id}/rating"), async (a) => readRating(a.env, a.request, "preset", a.at[0]!, await presetShown(a.env, a.at[0]!))],
  ["PUT", path("/api/v1/presets/{id}/rating"), (a) => giveRating(a.env, a.request, "preset", a.at[0]!, () => presetShown(a.env, a.at[0]!))],
  ["POST", path("/api/v1/presets/{id}/reports"), (a) => report(a.env, a.request, "preset", a.at[0]!, () => presetShown(a.env, a.at[0]!))],
  ["GET", path("/api/v1/tags"), (a) => listTags(a.env, a.admin)],

  ["GET", path("/api/v1/plugins"), (a) => listPlugins(a.env, a.url, a.admin)],
  ["POST", path("/api/v1/plugins"), (a) => submitPlugin(a.env, a.request, a.ctx)],
  ["GET", path("/api/v1/plugins/{id}"), (a) => getPlugin(a.env, a.at[0]!, a.admin)],
  ["GET", path("/api/v1/plugins/{id}/file"), (a) => pluginFileOf(a.env, a.url, a.at[0]!, a.admin)],
  ["GET", path("/api/v1/plugins/{id}/preview"), (a) => pluginPreviewOf(a.env, a.at[0]!, a.admin)],
  ["GET", path("/api/v1/plugins/{id}/rating"), async (a) => readRating(a.env, a.request, "plugin", a.at[0]!, await pluginShown(a.env, a.at[0]!))],
  ["PUT", path("/api/v1/plugins/{id}/rating"), (a) => giveRating(a.env, a.request, "plugin", a.at[0]!, () => pluginShown(a.env, a.at[0]!))],
  ["POST", path("/api/v1/plugins/{id}/reports"), (a) => report(a.env, a.request, "plugin", a.at[0]!, () => pluginShown(a.env, a.at[0]!))],

  ["POST", path("/api/v1/letters"), (a) => writeLetter(a.env, a.request)],

  // Every page asks whether the visitor is the admin; access says the sign-in is Cloudflare Access's.
  ["GET", path("/api/v1/admin"), (a) => json({ enabled: adminEnabled(a.env), signedIn: a.admin, access: true })],

  ["GET", path("/media/([^/]+)"), (a) => serveMedia(a.env, a.request, a.url, a.at[0]!)],
];

/** What only the admin may ask, under the one prefix Access guards: the admin's tools, Validate, the render and the defaults. */
const ADMIN: Route[] = [
  // Access keeps the session; this only drops the cookie the pages are signed in with.
  ["DELETE", path("/api/v1/admin/session"), () =>
    new Response(null, { status: 204, headers: { "Set-Cookie": "CF_Authorization=; Path=/; Max-Age=0; Secure; HttpOnly" } })],

  ["GET", path("/api/v1/admin/unchecked"), (a) => listUnchecked(a.env)],
  ["GET", path("/api/v1/admin/presets"), (a) => listCheckedPresets(a.env)],
  ["GET", path("/api/v1/admin/presets/{id}/file"), (a) => anyFile(a.env, "preset", a.at[0]!)],
  ["PUT", path("/api/v1/admin/presets/{id}/check"), (a) => checkPreset(a.env, a.request, a.at[0]!)],
  ["PUT", path("/api/v1/admin/presets/{id}/lacks"), (a) => putLacks(a.env, a.request, a.at[0]!)],
  ["PATCH", path("/api/v1/admin/presets/{id}"), (a) => changePreset(a.env, a.request, a.at[0]!)],
  ["DELETE", path("/api/v1/admin/presets/{id}"), (a) => deletePreset(a.env, a.at[0]!)],
  ["PUT", path("/api/v1/admin/presets/{id}/media/([^/]+)"), (a) => putMedia(a.env, a.request, a.at[0]!, a.at[1]!)],
  ["DELETE", path("/api/v1/admin/presets/{id}/media"), (a) => clearMedia(a.env, a.at[0]!)],

  ["GET", path("/api/v1/admin/plugins/{id}/file"), (a) => anyFile(a.env, "plugin", a.at[0]!)],
  ["PUT", path("/api/v1/admin/plugins/{id}/check"), (a) => checkPlugin(a.env, a.request, a.at[0]!)],
  ["PATCH", path("/api/v1/admin/plugins/{id}"), (a) => changePlugin(a.env, a.request, a.at[0]!)],
  ["DELETE", path("/api/v1/admin/plugins/{id}"), (a) => deletePlugin(a.env, a.at[0]!)],

  ["GET", path("/api/v1/admin/reports"), (a) => listReports(a.env)],
  ["DELETE", path("/api/v1/admin/reports/{id}"), (a) => dismissReport(a.env, a.at[0]!)],
  ["GET", path("/api/v1/admin/letters"), (a) => listLetters(a.env)],
  ["DELETE", path("/api/v1/admin/letters/{id}"), (a) => dismissLetter(a.env, a.at[0]!)],

  ["PUT", path("/api/v1/admin/defaults/([^/]+)"), (a) => putDefault(a.env, a.request, a.at[0]!)],
  ["PUT", path("/api/v1/admin/assets/(.+)"), (a) => putLargeFile(a.env, a.request, a.at[0]!)],
];

async function answer(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
  const url = new URL(request.url);
  const admin = await isAdmin(request, env);
  const pathname = url.pathname;

  if (pathname.startsWith("/api/v1/admin/")) {
    if (!admin) return unauthorized();
    return route(ADMIN, { request, env, ctx, url, at: [], admin });
  }

  if (pathname.startsWith("/api/") || pathname.startsWith("/media/"))
    return readCached(env, ctx, request, url, admin, () => route(PUBLIC, { request, env, ctx, url, at: [], admin }));

  if (pathname === "/admin.html" && !admin)
    return new Response("Sign in through Cloudflare Access to reach the admin page.", { status: 401 });

  return asset(request, env, url);
}

async function route(routes: Route[], asked: Asked): Promise<Response> {
  const pathname = asked.url.pathname;
  let matched = false;

  for (const [method, pattern, handle] of routes) {
    const found = pattern.exec(pathname);
    if (found === null) continue;

    matched = true;
    if (method !== asked.request.method && !(method === "GET" && asked.request.method === "HEAD")) continue;

    let at: string[];
    try {
      at = found.slice(1).map((part) => decodeURIComponent(part));
    } catch {
      return notFound();
    }

    return handle({ ...asked, at });
  }

  return matched ? new Response(null, { status: 405 }) : notFound();
}

/**
 * A page or a file: a folder's index.html, and the fingerprinted framework files kept
 * for good, from R2 where one was too large to be an asset.
 */
async function asset(request: Request, env: Env, url: URL): Promise<Response> {
  if (url.pathname.endsWith("/")) {
    const index = new URL(url.pathname + "index.html", url);
    return env.ASSETS.fetch(new Request(index, request));
  }

  const served = await env.ASSETS.fetch(request);
  if (!FRAMEWORK.test(url.pathname)) return served;

  if (served.status === 404) return (await largeFile(env, request, url.pathname)) ?? served;

  if (!served.ok) return served;

  const kept = new Response(served.body, served);
  kept.headers.set("Cache-Control", KEPT_FOR_GOOD);
  return kept;
}

/** What a browser that came over HTTPS is told to keep to, as the .NET site told it. */
const HSTS = "max-age=2592000";

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const response = await answer(request, env, ctx);
    if (new URL(request.url).protocol !== "https:") return response;

    const kept = new Response(response.body, response);
    kept.headers.set("Strict-Transport-Security", HSTS);
    return kept;
  },

  async scheduled(_controller: ScheduledController, env: Env): Promise<void> {
    await forgetRefused(env);
    await forgetSpentWindows(env);
  },
} satisfies ExportedHandler<Env>;
