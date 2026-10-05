import { createExecutionContext, env, waitOnExecutionContext } from "cloudflare:test";
import { vi } from "vitest";
import worker from "../src/index";

/** The site's origin in the tests. */
export const ORIGIN = "https://flyback.test";

let address = 0;

/** A fresh visitor address, so one test's rate limits are not another's. */
export const freshAddress = (): string => `198.51.${(address >> 8) & 255}.${address++ & 255}`;

export interface Asking extends RequestInit {
  /** The visitor's address, as Cloudflare would say it. */
  from?: string;
  /** Variables set for this request alone, such as a lower rate limit. */
  vars?: Record<string, string>;
}

/** Asks the Worker, as the given visitor, with whatever headers the test adds. */
export async function ask(path: string, init: Asking = {}): Promise<Response> {
  const { from, vars, ...rest } = init;
  const headers = new Headers(rest.headers);
  if (!headers.has("CF-Connecting-IP")) headers.set("CF-Connecting-IP", from ?? "192.0.2.1");

  const ctx = createExecutionContext();
  const response = await worker.fetch(new Request(ORIGIN + path, { ...rest, headers }), { ...env, ...vars }, ctx);
  await waitOnExecutionContext(ctx);
  return response;
}

export const askJson = async <T = any>(path: string, init?: Asking): Promise<T> =>
  (await (await ask(path, init)).json()) as T;

/** Asks as the admin, with a JSON body where one is given. */
export async function admin(path: string, method = "GET", sent?: unknown): Promise<Response> {
  return ask(path, {
    method,
    headers: await asAdmin(sent === undefined ? {} : { "Content-Type": "application/json" }),
    body: sent === undefined ? undefined : JSON.stringify(sent),
  });
}

/** A form holding the file, and a name where one is given. */
export function form(fileName: string, content: string | Uint8Array, name?: string): FormData {
  const data = new FormData();
  data.set("file", new File([content], fileName));
  if (name !== undefined) data.set("name", name);
  return data;
}

export const PATCH = '{"Nodes":[{"Id":1,"Type":"Oscillator"}],"Wires":[]}';

// ---- Access ----------------------------------------------------------------

const keys = (await crypto.subtle.generateKey(
  { name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
  true,
  ["sign", "verify"],
)) as CryptoKeyPair;
const publicJwk = { ...(await crypto.subtle.exportKey("jwk", keys.publicKey)), kid: "test-key" };

const realFetch = globalThis.fetch;

/** Access's certificates, as the team's address serves them; anything else fetched is refused. */
export const outbound = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
  const url = new Request(input, init).url;
  if (url === `${env.ACCESS_TEAM_DOMAIN}/cdn-cgi/access/certs`) return Response.json({ keys: [publicJwk] });
  if (url.startsWith("https://api.github.com/")) return new Response(null, { status: 204 });
  return realFetch(input, init);
});

const base64url = (bytes: Uint8Array | string): string =>
  btoa(typeof bytes === "string" ? bytes : String.fromCharCode(...bytes))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");

/** A token as Access signs one, with claims the test may override. */
export async function token(claims: Record<string, unknown> = {}, kid = "test-key"): Promise<string> {
  const seconds = Math.floor(Date.now() / 1000);
  const header = base64url(JSON.stringify({ alg: "RS256", kid, typ: "JWT" }));
  const payload = base64url(
    JSON.stringify({
      aud: [env.ACCESS_AUD],
      iss: env.ACCESS_TEAM_DOMAIN,
      email: "admin@example.org",
      iat: seconds,
      nbf: seconds,
      exp: seconds + 3600,
      ...claims,
    }),
  );
  const signature = new Uint8Array(
    await crypto.subtle.sign("RSASSA-PKCS1-v1_5", keys.privateKey, new TextEncoder().encode(header + "." + payload)),
  );
  return `${header}.${payload}.${base64url(signature)}`;
}

/** Headers that make the request the admin's, as Access sends them on a path it guards. */
export const asAdmin = async (headers: HeadersInit = {}): Promise<Headers> => {
  const signed = new Headers(headers);
  signed.set("Cf-Access-Jwt-Assertion", await token());
  return signed;
};

/** Submits a preset as a visitor would, returning what the site answered. */
export async function submit(fileName = "patch.fbk", content: string | Uint8Array = PATCH, name?: string, from = freshAddress()) {
  return ask("/api/v1/presets", { method: "POST", body: form(fileName, content, name), from });
}

/** Submits a preset and marks it checked as Validate would, returning its id. */
export interface Described {
  author?: string | null;
  description?: string | null;
  tags?: string[];
  lacks?: unknown;
  fileName?: string;
}

export async function published(
  name: string,
  { author = null, description = null, tags = [], lacks = null, fileName = "patch.fbk" }: Described = {},
): Promise<string> {
  const { id } = (await (await submit(fileName, PATCH, name)).json()) as { id: string };

  const checked = await admin(`/api/v1/admin/presets/${id}/check`, "PUT", { accepted: true, name, author, description, tags, lacks });
  if (checked.status !== 200) throw new Error(`check answered ${checked.status}: ${await checked.text()}`);

  return id;
}
