import { env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import { admin, ask, askJson, asAdmin, freshAddress, PATCH } from "./site";

const said = (name: string, more: Record<string, unknown> = {}) =>
  JSON.stringify({ accepted: true, name, author: "Flyback", description: "Shipped with the site.", tags: ["shipped"], lacks: null, ...more });

async function seed(fileName: string, content: string | Uint8Array, check: string) {
  const data = new FormData();
  data.set("file", new File([content], fileName));
  data.set("check", check);
  const answer = await ask(`/api/v1/admin/defaults/${encodeURIComponent(fileName)}`, { method: "PUT", headers: await asAdmin(), body: data });
  return { status: answer.status, said: answer.status === 200 ? await answer.json<{ id: string; state: string }>() : null };
}

describe("a default preset", () => {
  it("is added once, and the same file again changes nothing", async () => {
    const first = await seed("Machine Room.fbk", PATCH, said("Machine Room"));
    expect(first.said?.state).toBe("added");

    const again = await seed("Machine Room.fbk", PATCH, said("Machine Room"));
    expect(again.said).toEqual({ id: first.said!.id, state: "unchanged" });

    const listed = await askJson("/api/v1/presets");
    expect(listed.total).toBe(1);
    expect(listed.items[0]).toMatchObject({ name: "Machine Room", author: "Flyback", tags: ["shipped"], status: "checked", published: true });
  });

  it("is replaced under its id when the file changes, keeping the name the admin gave it", async () => {
    const { said: first } = await seed("Drone.fbk", PATCH, said("Drone"));
    await admin(`/api/v1/admin/presets/${first!.id}`, "PATCH", { name: "Renamed" });

    const changed = PATCH.replace("Oscillator", "Noise");
    const { said: second } = await seed("Drone.fbk", changed, said("Drone", { description: "Noisier." }));

    expect(second).toEqual({ id: first!.id, state: "replaced" });
    expect(await askJson(`/api/v1/presets/${first!.id}`)).toMatchObject({ name: "Renamed", description: "Noisier." });
    expect(new TextDecoder().decode(await (await ask(`/api/v1/presets/${first!.id}/file`)).arrayBuffer())).toBe(changed);
  });

  it("stays deleted once the admin deleted it", async () => {
    const { said: first } = await seed("Drone.fbk", PATCH, said("Drone"));
    await admin(`/api/v1/admin/presets/${first!.id}`, "DELETE");

    const { said: again } = await seed("Drone.fbk", PATCH + " ", said("Drone"));

    expect(again?.state).toBe("deleted");
    expect((await askJson("/api/v1/presets")).total).toBe(0);
  });

  it("is refused where the check refused it, or the name is not a default's", async () => {
    expect((await seed("Drone.fbk", PATCH, JSON.stringify({ accepted: false, reason: "Not a patch." }))).status).toBe(400);
    expect((await seed("Drone.exe", PATCH, said("Drone"))).status).toBe(400);
  });
});

describe("a default plugin", () => {
  const plugin = (more: Record<string, unknown> = {}) =>
    JSON.stringify({
      accepted: true,
      assembly: "Flyback.Plugins.Figures",
      name: "Figures",
      version: "1.0.0",
      author: "Flyback",
      description: "Shapes.",
      tags: [],
      adds: ["modules"],
      reaches: [],
      builds: ["any"],
      contract: {},
      modules: [],
      signer: "key",
      signerFingerprint: "f1",
      preview: null,
      ...more,
    });

  const PACKAGE = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 1, 2, 3]);

  it("is published from the start, and replaces a submitted copy of the same package", async () => {
    const data = new FormData();
    data.set("file", new File([PACKAGE], "figures.fbkp"));
    const submitted = await (await ask("/api/v1/plugins", { method: "POST", body: data, from: freshAddress() })).json<{ id: string }>();

    const { said: seeded } = await seed("Figures.fbkp", PACKAGE, plugin());

    expect(seeded?.state).toBe("added");
    expect((await ask(`/api/v1/plugins/${submitted.id}`)).status).toBe(404);
    expect((await askJson("/api/v1/plugins")).items.map((p: { name: string }) => p.name)).toEqual(["Figures"]);
  });

  it("keeps whether it is published when it is replaced", async () => {
    const { said: first } = await seed("Figures.fbkp", PACKAGE, plugin());
    await admin(`/api/v1/admin/plugins/${first!.id}`, "PATCH", { published: false });

    const { said: second } = await seed("Figures.fbkp", new Uint8Array([...PACKAGE, 4]), plugin({ version: "1.1.0" }));

    expect(second).toEqual({ id: first!.id, state: "replaced" });
    expect(await env.DB.prepare("SELECT published, version FROM plugins WHERE id = ?").bind(first!.id).first()).toEqual({
      published: 0,
      version: "1.1.0",
    });
  });
});
