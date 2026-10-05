import { describe, expect, it } from "vitest";
import { asAdmin, ask } from "./site";

describe("the pages", () => {
  it("are served from a folder's index", async () => {
    expect(await (await ask("/")).text()).toContain("The front page");
  });

  it("keep the viewer's fingerprinted files for good, and ask about the loader each time", async () => {
    const wasm = await ask("/viewer/_framework/dotnet.native.abcdef1234.wasm");
    expect(wasm.status).toBe(200);
    expect(wasm.headers.get("Cache-Control")).toBe("public, max-age=31536000, immutable");
    await wasm.arrayBuffer();

    const loader = await ask("/viewer/_framework/dotnet.js");
    expect(loader.status).toBe(200);
    expect(loader.headers.get("Cache-Control")).not.toContain("immutable");
    await loader.arrayBuffer();
  });

  it("serve a framework file too large to be an asset from R2, put there by the admin, kept for good", async () => {
    const path = "editor/_framework/dotnet.native.0123456789.wasm";
    expect((await ask("/" + path)).status).toBe(404);

    const put = await ask(`/api/v1/admin/assets/${path}`, { method: "PUT", headers: await asAdmin(), body: new Uint8Array([0, 0x61, 0x73, 0x6d]) });
    expect(put.status).toBe(204);

    const served = await ask("/" + path);
    expect(served.status).toBe(200);
    expect(served.headers.get("Content-Type")).toBe("application/wasm");
    expect(served.headers.get("Cache-Control")).toBe("public, max-age=31536000, immutable");
    expect(new Uint8Array(await served.arrayBuffer())).toEqual(new Uint8Array([0, 0x61, 0x73, 0x6d]));
  });

  it("keep in R2 only the viewer's and editor's fingerprinted framework files, and only from the admin", async () => {
    const put = async (path: string, admin = true) =>
      (await ask(`/api/v1/admin/assets/${path}`, { method: "PUT", headers: admin ? await asAdmin() : {}, body: "x" })).status;

    expect(await put("index.html")).toBe(400);
    expect(await put("editor/_framework/dotnet.js")).toBe(400);
    expect(await put("editor/_framework/../../index.html")).toBe(400);
    expect(await put("viewer/_framework/dotnet.native.0123456789.wasm", false)).toBe(401);
  });

  it("tell a browser that came over HTTPS to keep to it", async () => {
    expect((await ask("/presets.html")).headers.get("Strict-Transport-Security")).toBe("max-age=2592000");
    expect((await ask("/api/v1/presets")).headers.get("Strict-Transport-Security")).toBe("max-age=2592000");
  });

  it("answer a path the site does not have with 404", async () => {
    expect((await ask("/nothing-here.html")).status).toBe(404);
    expect((await ask("/api/v2/presets")).status).toBe(404);
  });
});
