import type { Env } from "./env";

/**
 * Whether the request carries a token Cloudflare Access signed for this site's admin
 * application: in the header Access adds to the paths it guards, or in the cookie it
 * leaves for the rest. Checked here rather than trusted to Access, so a path Access
 * was never told about is not an open door.
 */
export async function isAdmin(request: Request, env: Env): Promise<boolean> {
  const team = teamOf(env);
  if (team === null || !env.ACCESS_AUD) return false;

  const token = request.headers.get("Cf-Access-Jwt-Assertion") ?? cookie(request, "CF_Authorization");
  if (!token) return false;

  try {
    return await verified(token, team, env.ACCESS_AUD);
  } catch {
    return false;
  }
}

/** Whether admin mode is configured at all. */
export const adminEnabled = (env: Env): boolean => teamOf(env) !== null && !!env.ACCESS_AUD;

const teamOf = (env: Env): string | null => env.ACCESS_TEAM_DOMAIN?.replace(/\/+$/, "") || null;

function cookie(request: Request, name: string): string | null {
  for (const part of (request.headers.get("Cookie") ?? "").split(";")) {
    const [key, ...value] = part.trim().split("=");
    if (key === name) return value.join("=");
  }
  return null;
}

interface Claims {
  aud?: string | string[];
  iss?: string;
  exp?: number;
  nbf?: number;
}

const SKEW = 60;

async function verified(token: string, team: string, audience: string): Promise<boolean> {
  const parts = token.split(".");
  if (parts.length !== 3) return false;

  const header = JSON.parse(decode(parts[0]!)) as { alg?: string; kid?: string };
  if (header.alg !== "RS256" || !header.kid) return false;

  const key = await keyFor(team, header.kid);
  if (key === null) return false;

  const signed = await crypto.subtle.verify(
    "RSASSA-PKCS1-v1_5",
    key,
    bytes(parts[2]!),
    new TextEncoder().encode(parts[0] + "." + parts[1]),
  );
  if (!signed) return false;

  const claims = JSON.parse(decode(parts[1]!)) as Claims;
  const seconds = Date.now() / 1000;
  const audiences = Array.isArray(claims.aud) ? claims.aud : [claims.aud];

  return (
    audiences.includes(audience) &&
    claims.iss === team &&
    typeof claims.exp === "number" &&
    claims.exp > seconds - SKEW &&
    (claims.nbf === undefined || claims.nbf <= seconds + SKEW)
  );
}

/** The team's signing keys, kept for ten minutes, and fetched afresh for a key id not among them. */
let keys: { team: string; at: number; byId: Map<string, CryptoKey> } | null = null;

async function keyFor(team: string, kid: string): Promise<CryptoKey | null> {
  const fresh = keys !== null && keys.team === team && Date.now() - keys.at < 600_000;

  if (!fresh || !keys!.byId.has(kid)) {
    const response = await fetch(`${team}/cdn-cgi/access/certs`);
    if (!response.ok) return null;

    const listed = (await response.json()) as { keys?: (JsonWebKey & { kid?: string })[] };
    const byId = new Map<string, CryptoKey>();

    for (const jwk of listed.keys ?? []) {
      if (!jwk.kid || jwk.kty !== "RSA") continue;
      byId.set(
        jwk.kid,
        await crypto.subtle.importKey("jwk", jwk, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]),
      );
    }

    keys = { team, at: Date.now(), byId };
  }

  return keys!.byId.get(kid) ?? null;
}

function bytes(base64url: string): Uint8Array {
  const base64 = base64url.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(base64url.length / 4) * 4, "=");
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

const decode = (base64url: string): string => new TextDecoder().decode(bytes(base64url));
