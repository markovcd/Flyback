import { describe, expect, it } from "vitest";
import { admin, asAdmin, ask, askJson, form, freshAddress } from "./site";

const TEXT = (seed: string) => `pack example.lights 1.0.0\n// ${seed}\nmodule glow(a = 0) { out = a }\n`;

const sendPack = (fileName: string, content: string | Uint8Array) =>
  ask("/api/v1/packs", { method: "POST", body: form(fileName, content), from: freshAddress() });

const checked = (pack: string, more: Record<string, unknown> = {}) => ({
  accepted: true,
  pack,
  version: "1.0.0",
  modules: [{ id: `${pack}.glow`, name: "Glow" }],
  ...more,
});

/** Submits a pack and checks it as Validate would, returning its id. */
async function accepted(pack: string, more: Record<string, unknown> = {}): Promise<string> {
  const { id } = await (await sendPack("upload.fbks", TEXT(pack + JSON.stringify(more)))).json<{ id: string }>();
  const answer = await admin(`/api/v1/admin/packs/${id}/check`, "PUT", checked(pack, more));
  if (answer.status !== 200) throw new Error(await answer.text());
  return id;
}

const publish = (id: string) => admin(`/api/v1/admin/packs/${id}`, "PATCH", { published: true });

describe("a submitted pack", () => {
  it("waits unchecked, then unpublished until the admin publishes it", async () => {
    const sent = await sendPack("lights.fbks", TEXT("lights"));
    expect(sent.status).toBe(202);
    const { id, status, sha256 } = await sent.json<{ id: string; status: string; sha256: string }>();
    expect(status).toBe("unchecked");
    expect(sha256).toMatch(/^[0-9a-f]{64}$/);

    const waiting = await (await admin("/api/v1/admin/unchecked")).json<{ packs: { id: string }[] }>();
    expect(waiting.packs.map((p) => p.id)).toEqual([id]);

    await admin(`/api/v1/admin/packs/${id}/check`, "PUT", checked("example.lights"));

    expect((await askJson("/api/v1/packs")).total).toBe(0);
    expect((await ask(`/api/v1/packs/${id}`)).status).toBe(404);
    expect((await ask(`/api/v1/packs/${id}/file`)).status).toBe(404);

    expect((await publish(id)).status).toBe(200);

    const listed = await askJson("/api/v1/packs");
    expect(listed.items[0]).toMatchObject({
      id,
      pack: "example.lights",
      version: "1.0.0",
      fileName: "example.lights.fbks",
      modules: [{ id: "example.lights.glow", name: "Glow" }],
      published: true,
      status: "checked",
      file: `/api/v1/packs/${id}/file`,
    });
  });

  it("is refused at once when it is not a pack's text", async () => {
    expect((await sendPack("lights.txt", TEXT("x"))).status).toBe(400);
    expect((await sendPack("lights.fbks", new Uint8Array([0x70, 0x61, 0xff, 0xfe]))).status).toBe(400);
    expect((await sendPack("lights.fbks", new Uint8Array([0x70, 0x00, 0x61]))).status).toBe(400);
  });

  it("is refused when it is over a megabyte", async () => {
    expect((await sendPack("lights.fbks", "a".repeat((1 << 20) + 1))).status).toBe(413);
  });

  it("is refused when the same text was sent already", async () => {
    await sendPack("lights.fbks", TEXT("same"));
    const again = await sendPack("lights.fbks", TEXT("same"));

    expect(again.status).toBe(409);
    expect(await again.json()).toEqual({ error: "That pack has been sent already." });
  });

  it("says why it was refused", async () => {
    const { id } = await (await sendPack("lights.fbks", TEXT("wrong"))).json<{ id: string }>();

    await admin(`/api/v1/admin/packs/${id}/check`, "PUT", { accepted: false, reason: "line 3: 'glo' is not a module." });

    expect(await askJson(`/api/v1/packs/${id}`)).toMatchObject({ status: "refused", reason: "line 3: 'glo' is not a module." });
  });

  it("refuses a check that does not name a pack, a version and its modules", async () => {
    const { id } = await (await sendPack("lights.fbks", TEXT("bad check"))).json<{ id: string }>();
    const check = (more: Record<string, unknown>) => admin(`/api/v1/admin/packs/${id}/check`, "PUT", checked("example.lights", more));

    expect((await check({ pack: "Lights" })).status).toBe(400);
    expect((await check({ version: "one" })).status).toBe(400);
    expect((await check({ modules: [] })).status).toBe(400);
    expect((await check({ modules: [{ id: 1 }] })).status).toBe(400);
    expect((await askJson(`/api/v1/packs/${id}`)).status).toBe("unchecked");
  });

  it("is checked once", async () => {
    const id = await accepted("example.lights");

    expect((await admin(`/api/v1/admin/packs/${id}/check`, "PUT", checked("example.lights"))).status).toBe(409);
  });
});

describe("the pack shelf", () => {
  it("filters by module and words", async () => {
    await publish(await accepted("example.lights"));
    await publish(await accepted("example.kites", { modules: [{ id: "example.kites.string", name: "String" }] }));

    const packs = async (query: string) => (await askJson(`/api/v1/packs?${query}`)).items.map((p: { pack: string }) => p.pack);

    expect(await packs("")).toEqual(["example.kites", "example.lights"]);
    expect(await packs("module=example.kites.string")).toEqual(["example.kites"]);
    expect(await packs("module=example.kites")).toEqual([]);
    expect(await packs("q=string")).toEqual(["example.kites"]);
  });

  it("lists a pack once, at its newest published version, and every version by its id", async () => {
    const first = await accepted("example.lights", { version: "1.0.0" });
    const second = await accepted("example.lights", { version: "1.1.0" });
    const draft = await accepted("example.lights", { version: "1.2.0" });
    await publish(first);
    await publish(second);

    const shelf = await askJson("/api/v1/packs");
    expect(shelf.items.map((p: { id: string }) => p.id)).toEqual([second]);
    expect(shelf.items[0]).toMatchObject({ version: "1.1.0", versions: 2 });

    const all = await askJson("/api/v1/packs?pack=example.lights");
    expect(all.items.map((p: { version: string }) => p.version)).toEqual(["1.1.0", "1.0.0"]);

    const reviewed = await (await ask("/api/v1/packs", { headers: await asAdmin() })).json<{ items: { id: string }[] }>();
    expect(reviewed.items.map((p) => p.id).sort()).toEqual([first, second, draft].sort());
  });

  it("downloads a pack's text, counted", async () => {
    const id = await accepted("example.lights");
    await publish(id);

    const file = await ask(`/api/v1/packs/${id}/file`);
    expect(file.headers.get("Content-Disposition")).toContain('filename="example.lights.fbks"');
    expect(await file.text()).toBe(TEXT("example.lights{}"));
    expect((await askJson(`/api/v1/packs/${id}`)).downloads).toBe(1);
  });

  it("takes a report and a rating, and forgets them with the pack", async () => {
    const id = await accepted("example.lights");
    await publish(id);

    await ask(`/api/v1/packs/${id}/reports`, { method: "POST", headers: { "Content-Type": "application/json" }, body: '{"reason":"broken"}' });
    expect(await (await admin("/api/v1/admin/reports")).json()).toMatchObject([{ kind: "pack", subject: id, name: "example.lights" }]);

    const rated = await ask(`/api/v1/packs/${id}/rating`, {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Sec-Fetch-Site": "same-origin" },
      body: '{"stars":4}',
    });
    expect(rated.status).toBe(200);
    expect((await askJson(`/api/v1/packs/${id}`)).rating).toEqual({ average: 4, count: 1 });

    expect((await admin(`/api/v1/admin/packs/${id}`, "DELETE")).status).toBe(204);
    expect(await (await admin("/api/v1/admin/reports")).json()).toEqual([]);
    expect((await ask(`/api/v1/packs/${id}/file`)).status).toBe(404);
  });

  it("hands Validate a pack's file whatever its state", async () => {
    const { id } = await (await sendPack("lights.fbks", TEXT("waiting"))).json<{ id: string }>();

    const file = await admin(`/api/v1/admin/packs/${id}/file`);
    expect(file.status).toBe(200);
    expect(await file.text()).toBe(TEXT("waiting"));
  });
});
