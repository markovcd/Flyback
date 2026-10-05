import { describe, expect, it } from "vitest";
import { admin, ask, askJson, form, PATCH, published, submit } from "./site";

const names = (page: { items: { name: string }[] }) => page.items.map((i) => i.name);

describe("a submitted preset", () => {
  it("is taken at once and waits, listed nowhere, until it has been checked", async () => {
    const sent = await submit("Drone.fbk", PATCH, "Night bus");

    expect(sent.status).toBe(202);
    const entry = await sent.json<{ id: string; status: string; name: string }>();
    expect(entry.status).toBe("unchecked");
    expect(sent.headers.get("Location")).toBe(`/api/v1/presets/${entry.id}`);

    expect((await askJson("/api/v1/presets")).total).toBe(0);
    expect((await askJson("/api/v1/presets?pending=true")).total).toBe(0);
    expect((await ask(`/api/v1/presets/${entry.id}/file`)).status).toBe(404);
    expect((await ask(`/api/v1/presets/${entry.id}/rating`)).status).toBe(404);
  });

  it("is listed with what the check said about it once it has been checked", async () => {
    await published("Drone", { author: "Ada", description: "A slow drone.", tags: ["ambient", "drone"] });

    const list = await askJson("/api/v1/presets");
    const item = list.items[0];

    expect(list.total).toBe(1);
    expect(item).toMatchObject({
      name: "Drone",
      author: "Ada",
      description: "A slow drone.",
      tags: ["ambient", "drone"],
      fileName: "patch.fbk",
      downloads: 0,
      published: true,
      status: "checked",
      lacks: null,
      rating: { average: 0, count: 0 },
      media: { still: null, loop: null, audio: null, peaks: null, state: "pending" },
    });
    expect(item.file).toBe(`/api/v1/presets/${item.id}/file`);
    expect(item.submitted).toMatch(/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z$/);
  });

  it("says why it was refused to whoever has its id, and is listed nowhere", async () => {
    const { id } = await (await submit("notes.fbk", '{"hello": 1}')).json<{ id: string }>();

    const refused = await admin(`/api/v1/admin/presets/${id}/check`, "PUT", {
      accepted: false,
      reason: "That is not a Flyback patch. Send a .fbk or .fbkb file.",
    });
    expect(refused.status).toBe(200);

    const entry = await askJson(`/api/v1/presets/${id}`);
    expect(entry.status).toBe("refused");
    expect(entry.reason).toBe("That is not a Flyback patch. Send a .fbk or .fbkb file.");
    expect((await askJson("/api/v1/presets")).total).toBe(0);
  });

  it("names the plugins the web pages lack, as the check said", async () => {
    const lacks = { plugins: [{ id: "example.lantern", name: "Lantern" }], modules: 1, said: "Needs the Lantern plugin" };
    const id = await published("Lantern", { lacks });

    expect((await askJson(`/api/v1/presets/${id}`)).lacks).toEqual(lacks);
  });

  it.each([
    ["notes.txt", '{"Nodes": [{}]}'],
    ["broken.fbkb", "not a zip"],
    ["notes.fbk", "not json at all"],
    ["notes.fbk", "[1, 2]"],
  ])("is refused at once when %s is plainly not a patch", async (fileName, text) => {
    const sent = await submit(fileName, text);

    expect(sent.status).toBe(400);
    expect(await sent.json()).toEqual({ error: "That is not a Flyback patch. Send a .fbk or .fbkb file." });
  });

  it("is taken with a byte order mark", async () => {
    const marked = new Uint8Array([0xef, 0xbb, 0xbf, ...new TextEncoder().encode(PATCH)]);
    expect((await submit("Drone.fbk", marked)).status).toBe(202);
  });

  it("is taken as a bundle when it is a zip", async () => {
    expect((await submit("Drone.fbkb", new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0, 0]))).status).toBe(202);
  });

  it("is refused when it is not a form, or has no file", async () => {
    const json = await ask("/api/v1/presets", { method: "POST", body: PATCH, headers: { "Content-Type": "application/json" } });
    expect(json.status).toBe(400);
    expect(await json.json()).toEqual({ error: "Send the preset as a form with a file field." });

    const empty = new FormData();
    empty.set("name", "Nothing");
    const none = await ask("/api/v1/presets", { method: "POST", body: empty });
    expect(await none.json()).toEqual({ error: "There is no file in the form." });
  });

  it("is refused past twenty megabytes", async () => {
    const large = new Uint8Array(20 * 1024 * 1024 + 1);
    large.set(new TextEncoder().encode(PATCH));

    expect((await submit("Large.fbk", large)).status).toBe(413);
  });

  it("is taken however many arrive at once", async () => {
    const statuses = await Promise.all(Array.from({ length: 16 }, (_, i) => submit(`Drone${i}.fbk`).then((r) => r.status)));

    expect(statuses.every((s) => s === 202)).toBe(true);
    expect((await (await admin("/api/v1/admin/unchecked")).json<{ presets: unknown[] }>()).presets).toHaveLength(16);
  });
});

describe("a shared preset", () => {
  it("downloads as the file that was submitted, counted unless render-presets asks", async () => {
    const id = await published("Drone");

    const first = await ask(`/api/v1/presets/${id}/file`);
    expect(new TextDecoder().decode(await first.arrayBuffer())).toBe(PATCH);
    expect(first.headers.get("Content-Disposition")).toContain('filename="patch.fbk"');

    await ask(`/api/v1/presets/${id}/file?count=false`);

    expect((await askJson(`/api/v1/presets/${id}`)).downloads).toBe(1);
  });

  it("is found by tag and by words, and the tags are counted", async () => {
    await published("Rain", { description: "Rain on a tin roof.", tags: ["ambient"] });
    await published("Kick", { description: "A four-to-the-floor kick.", tags: ["techno"] });

    expect(names(await askJson("/api/v1/presets?tag=techno"))).toEqual(["Kick"]);
    expect(names(await askJson("/api/v1/presets?tag=%20TECHNO%20"))).toEqual(["Kick"]);
    expect(names(await askJson("/api/v1/presets?q=tin%20roof"))).toEqual(["Rain"]);
    expect(names(await askJson("/api/v1/presets?q=tin%20kick"))).toEqual([]);
    expect(await askJson("/api/v1/tags")).toEqual([
      { tag: "ambient", count: 1 },
      { tag: "techno", count: 1 },
    ]);
  });

  it("is found whatever the case of its name, beyond ASCII", async () => {
    await published("\u0141\u00f3d\u017a nights");

    expect((await askJson("/api/v1/presets?q=" + encodeURIComponent("\u0142\u00f3d\u017a"))).total).toBe(1);
  });

  it("is not found by a word that is only LIKE's wildcard", async () => {
    await published("Drone");

    expect((await askJson("/api/v1/presets?q=%25")).total).toBe(0);
    expect((await askJson("/api/v1/presets?q=_")).total).toBe(0);
  });

  it("lists newest first, a page at a time, and a page far past the last is empty", async () => {
    for (const name of ["One", "Two", "Three"]) await published(name);

    expect(names(await askJson("/api/v1/presets"))).toEqual(["Three", "Two", "One"]);
    expect((await askJson("/api/v1/presets?page=89478487")).items).toEqual([]);
    expect((await ask("/api/v1/presets?page=two")).status).toBe(400);
  });

  it("is hidden from everyone but the admin while unpublished", async () => {
    const id = await published("Rain", { tags: ["ambient"] });

    expect((await admin(`/api/v1/admin/presets/${id}`, "PATCH", { published: false })).status).toBe(200);

    expect(names(await askJson("/api/v1/presets"))).toEqual([]);
    expect(await askJson("/api/v1/tags")).toEqual([]);
    expect((await ask(`/api/v1/presets/${id}`)).status).toBe(404);
    expect((await ask(`/api/v1/presets/${id}/file`)).status).toBe(404);
    expect((await askJson("/api/v1/presets?pending=true")).items).toEqual([]);

    const seen = await (await admin("/api/v1/presets")).json<{ items: { published: boolean }[] }>();
    expect(seen.items.map((i) => i.published)).toEqual([false]);

    await admin(`/api/v1/admin/presets/${id}`, "PATCH", { published: true });
    expect(names(await askJson("/api/v1/presets"))).toEqual(["Rain"]);
  });

  it("is renamed by the admin, tidied, and found by its new name", async () => {
    const id = await published("Drone");

    const renamed = await admin(`/api/v1/admin/presets/${id}`, "PATCH", { name: '  Night   "bus" ' });
    expect(renamed.status).toBe(200);

    expect(names(await askJson("/api/v1/presets?q=night"))).toEqual(["Night \u201cbus\u201d"]);
    expect((await admin(`/api/v1/admin/presets/${id}`, "PATCH", { name: "   " })).status).toBe(400);
    expect((await admin("/api/v1/admin/presets/nothing-here", "PATCH", { name: "Night" })).status).toBe(404);
  });

  it("keeps a long name whole to the character", async () => {
    const id = await published("Drone");

    await admin(`/api/v1/admin/presets/${id}`, "PATCH", { name: "a".repeat(59) + "\u{1f3b9}" });

    const name: string = (await askJson(`/api/v1/presets/${id}`)).name;
    expect(name).toBe("a".repeat(59));
  });

  it("is deleted by the admin with its file and its tags", async () => {
    const id = await published("Rain", { tags: ["ambient"] });

    expect((await admin(`/api/v1/admin/presets/${id}`, "DELETE")).status).toBe(204);

    expect((await askJson("/api/v1/presets")).total).toBe(0);
    expect(await askJson("/api/v1/tags")).toEqual([]);
    expect((await admin(`/api/v1/admin/presets/${id}`, "DELETE")).status).toBe(404);
  });

  it("is changed and deleted by nobody else", async () => {
    const id = await published("Drone");

    const change = await ask(`/api/v1/admin/presets/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: "Mine now", published: false }),
    });
    expect(change.status).toBe(401);
    expect((await ask(`/api/v1/admin/presets/${id}`, { method: "DELETE" })).status).toBe(401);

    // The routes the .NET site took these on are not routes here.
    expect((await ask(`/api/v1/presets/${id}`, { method: "DELETE" })).status).toBe(405);

    expect(names(await askJson("/api/v1/presets"))).toEqual(["Drone"]);
  });

  it("is never served from a form whose name field holds the file", async () => {
    const data = form("Drone.fbk", PATCH);
    data.set("name", new File(["x"], "name.txt"));

    const sent = await ask("/api/v1/presets", { method: "POST", body: data });
    expect((await sent.json<{ name: string }>()).name).toBe("Drone");
  });
});
