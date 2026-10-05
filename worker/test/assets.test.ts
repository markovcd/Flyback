import { describe, expect, it } from "vitest";
import { ask } from "./site";

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

  it("tell a browser that came over HTTPS to keep to it", async () => {
    expect((await ask("/presets.html")).headers.get("Strict-Transport-Security")).toBe("max-age=2592000");
    expect((await ask("/api/v1/presets")).headers.get("Strict-Transport-Security")).toBe("max-age=2592000");
  });

  it("answer a path the site does not have with 404", async () => {
    expect((await ask("/nothing-here.html")).status).toBe(404);
    expect((await ask("/api/v2/presets")).status).toBe(404);
  });
});
