import { describe, expect, it } from "vitest";
import { ask, askJson, asAdmin, token } from "./site";

const withToken = (value: string) => ask("/api/v1/admin/unchecked", { headers: { "Cf-Access-Jwt-Assertion": value } });

describe("the admin", () => {
  it("is whoever carries a token Access signed for this site", async () => {
    expect((await ask("/api/v1/admin/unchecked", { headers: await asAdmin() })).status).toBe(200);
  });

  it("is nobody without a token, or with one meant for something else", async () => {
    expect((await ask("/api/v1/admin/unchecked")).status).toBe(401);
    expect((await withToken(await token({ aud: ["another-application"] }))).status).toBe(401);
    expect((await withToken(await token({ iss: "https://elsewhere.cloudflareaccess.com" }))).status).toBe(401);
    expect((await withToken(await token({ exp: Math.floor(Date.now() / 1000) - 3600 }))).status).toBe(401);
    expect((await withToken(await token({}, "a-key-nobody-has")))).toHaveProperty("status", 401);
    expect((await withToken("not.a.token")).status).toBe(401);
  });

  it("is nobody with a token whose claims were changed after it was signed", async () => {
    const [header, , signature] = (await token()).split(".");
    const claims = btoa(JSON.stringify({ aud: ["flyback-test-audience"], iss: "https://flyback-test.cloudflareaccess.com", exp: 9999999999 }))
      .replace(/=+$/, "")
      .replace(/\+/g, "-")
      .replace(/\//g, "_");

    expect((await withToken(`${header}.${claims}.${signature}`)).status).toBe(401);
  });

  it("is signed in on the public pages by the cookie Access leaves", async () => {
    const cookie = `other=1; CF_Authorization=${await token()}`;

    expect(await askJson("/api/v1/admin", { headers: { Cookie: cookie } })).toEqual({ enabled: true, signedIn: true, access: true });
    expect(await askJson("/api/v1/admin")).toEqual({ enabled: true, signedIn: false, access: true });
  });

  it("is off where Access is not configured", async () => {
    const answer = await askJson("/api/v1/admin", { vars: { ACCESS_AUD: "" }, headers: await asAdmin() });

    expect(answer).toEqual({ enabled: false, signedIn: false, access: true });
  });

  it("reaches the admin page only signed in", async () => {
    expect((await ask("/admin.html")).status).toBe(401);
    expect(await (await ask("/admin.html", { headers: await asAdmin() })).text()).toContain("The admin page");
  });

  it("signs out by dropping the cookie", async () => {
    const out = await ask("/api/v1/admin/session", { method: "DELETE", headers: await asAdmin() });

    expect(out.status).toBe(204);
    expect(out.headers.get("Set-Cookie")).toMatch(/^CF_Authorization=;.*Max-Age=0/);
  });
});
