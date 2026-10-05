import { describe, expect, it } from "vitest";
import { admin, ask, askJson, form, freshAddress } from "./site";

/** A zip's first bytes and something after them, unique to seed. */
const PACKAGE = (seed: string) => new Uint8Array([0x50, 0x4b, 0x03, 0x04, ...new TextEncoder().encode(seed)]);

const sendPlugin = (fileName: string, bytes: Uint8Array) =>
  ask("/api/v1/plugins", { method: "POST", body: form(fileName, bytes), from: freshAddress() });

const checked = (assembly: string, more: Record<string, unknown> = {}) => ({
  accepted: true,
  assembly,
  name: assembly.replace(/^.*\./, ""),
  version: "1.0.0",
  author: "Ada",
  description: "Lights for the canvas.",
  tags: ["light"],
  adds: ["modules"],
  reaches: [],
  builds: ["any"],
  contract: { "Flyback.Core": "1.2.0", "Flyback.Plugins": "1.2.0" },
  modules: [{ id: "example.lantern.glow", name: "Glow" }],
  signer: "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE",
  signerFingerprint: "ab12",
  preview: null,
  ...more,
});

/** Submits a package and checks it as Validate would, returning its id. */
async function accepted(assembly: string, more: Record<string, unknown> = {}): Promise<string> {
  const { id } = await (await sendPlugin("upload.fbkp", PACKAGE(assembly + JSON.stringify(more)))).json<{ id: string }>();
  const answer = await admin(`/api/v1/admin/plugins/${id}/check`, "PUT", checked(assembly, more));
  if (answer.status !== 200) throw new Error(await answer.text());
  return id;
}

const publish = (id: string) => admin(`/api/v1/admin/plugins/${id}`, "PATCH", { published: true });

describe("a submitted plugin", () => {
  it("waits unchecked, then unpublished until the admin publishes it", async () => {
    const sent = await sendPlugin("lantern.fbkp", PACKAGE("lantern"));
    expect(sent.status).toBe(202);
    const { id, status, sha256 } = await sent.json<{ id: string; status: string; sha256: string }>();
    expect(status).toBe("unchecked");
    expect(sha256).toMatch(/^[0-9a-f]{64}$/);

    await admin(`/api/v1/admin/plugins/${id}/check`, "PUT", checked("Example.Lantern"));

    expect((await askJson("/api/v1/plugins")).total).toBe(0);
    expect((await ask(`/api/v1/plugins/${id}`)).status).toBe(404);
    expect((await ask(`/api/v1/plugins/${id}/file`)).status).toBe(404);

    expect((await publish(id)).status).toBe(200);

    const listed = await askJson("/api/v1/plugins");
    expect(listed.items[0]).toMatchObject({
      id,
      assembly: "Example.Lantern",
      name: "Lantern",
      fileName: "Example.Lantern.fbkp",
      contract: { "Flyback.Core": "1.2.0", "Flyback.Plugins": "1.2.0" },
      modules: [{ id: "example.lantern.glow", name: "Glow" }],
      signer: "ab12",
      builds: ["any"],
      published: true,
      preview: null,
      status: "checked",
    });
  });

  it("is refused at once when it is not a package", async () => {
    expect((await sendPlugin("lantern.zip", PACKAGE("x"))).status).toBe(400);
    expect((await sendPlugin("lantern.fbkp", new TextEncoder().encode("not a zip"))).status).toBe(400);
  });

  it("is refused when the same package was sent already", async () => {
    await sendPlugin("lantern.fbkp", PACKAGE("same"));
    const again = await sendPlugin("lantern.fbkp", PACKAGE("same"));

    expect(again.status).toBe(409);
    expect(await again.json()).toEqual({ error: "That package has been sent already." });
  });

  it("says why it was refused", async () => {
    const { id } = await (await sendPlugin("lantern.fbkp", PACKAGE("unsigned"))).json<{ id: string }>();

    await admin(`/api/v1/admin/plugins/${id}/check`, "PUT", { accepted: false, reason: "It is not signed." });

    expect(await askJson(`/api/v1/plugins/${id}`)).toMatchObject({ status: "refused", reason: "It is not signed." });
  });

  it("is refused when a published plugin of its name was signed with another key", async () => {
    await publish(await accepted("Example.Lantern", { signer: "first-key" }));

    const { id } = await (await sendPlugin("lantern.fbkp", PACKAGE("impostor"))).json<{ id: string }>();
    await admin(`/api/v1/admin/plugins/${id}/check`, "PUT", checked("example.lantern", { signer: "second-key" }));

    expect(await askJson(`/api/v1/plugins/${id}`)).toMatchObject({
      status: "refused",
      reason: "A published plugin is already called example.lantern, and was signed with another key.",
    });
  });

  it("is not published over another key's plugin of the same name", async () => {
    const first = await accepted("Example.Lantern", { signer: "first-key" });
    const second = await accepted("Example.Lantern", { signer: "second-key" });

    expect((await publish(first)).status).toBe(200);
    expect((await publish(second)).status).toBe(409);
  });

  it("serves its preview", async () => {
    const id = await accepted("Example.Lantern", { preview: { type: "image/png", data: btoa("\x89PNG") } });
    await publish(id);

    const preview = await ask(`/api/v1/plugins/${id}/preview`);
    expect(preview.headers.get("Content-Type")).toBe("image/png");
    expect(preview.headers.get("X-Content-Type-Options")).toBe("nosniff");
    expect(new Uint8Array(await preview.arrayBuffer())).toEqual(new Uint8Array([0x89, 0x50, 0x4e, 0x47]));
    expect((await askJson(`/api/v1/plugins/${id}`)).preview).toBe(`/api/v1/plugins/${id}/preview`);
  });

  it("refuses a check whose preview is not an image", async () => {
    const { id } = await (await sendPlugin("lantern.fbkp", PACKAGE("svg"))).json<{ id: string }>();

    const answer = await admin(`/api/v1/admin/plugins/${id}/check`, "PUT", checked("Example.Lantern", { preview: { type: "image/svg+xml", data: "" } }));
    expect(answer.status).toBe(400);
  });
});

describe("the plugin shelf", () => {
  it("filters by platform, tag, module and words", async () => {
    await publish(await accepted("Example.Lantern", { builds: ["win"], tags: ["light"] }));
    await publish(await accepted("Example.Kite", { builds: ["any"], tags: ["wind"], modules: [{ id: "example.kite.string", name: "String" }] }));

    const names = async (query: string) => (await askJson(`/api/v1/plugins?${query}`)).items.map((p: { name: string }) => p.name);

    expect(await names("platform=win")).toEqual(["Kite", "Lantern"]);
    expect(await names("platform=linux")).toEqual(["Kite"]);
    expect(await names("tag=wind")).toEqual(["Kite"]);
    expect(await names("module=example.kite.string")).toEqual(["Kite"]);
    expect(await names("module=example.kite")).toEqual([]);
    expect(await names("q=string")).toEqual(["Kite"]);
    expect((await ask("/api/v1/plugins?platform=amiga")).status).toBe(400);
  });

  it("downloads a package, counted", async () => {
    const id = await accepted("Example.Lantern");
    await publish(id);

    const file = await ask(`/api/v1/plugins/${id}/file`);
    expect(file.headers.get("Content-Disposition")).toContain('filename="Example.Lantern.fbkp"');
    expect((await askJson(`/api/v1/plugins/${id}`)).downloads).toBe(1);
  });

  it("forgets a deleted plugin's reports and ratings", async () => {
    const id = await accepted("Example.Lantern");
    await publish(id);

    await ask(`/api/v1/plugins/${id}/reports`, { method: "POST", headers: { "Content-Type": "application/json" }, body: '{"reason":"broken"}' });
    expect((await admin(`/api/v1/admin/plugins/${id}`, "DELETE")).status).toBe(204);

    expect(await (await admin("/api/v1/admin/reports")).json()).toEqual([]);
    expect((await ask(`/api/v1/plugins/${id}/file`)).status).toBe(404);
  });
});
