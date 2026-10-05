import { env } from "cloudflare:test";
import { beforeEach, describe, expect, it } from "vitest";
import presets from "./fixtures/contract/presets.json";
import plugins from "./fixtures/contract/plugins.json";
import rows from "./fixtures/export-site.sql?raw";
import { admin, ask, askJson } from "./site";

const LANTERN = "0199a000000070008000000000000001";
const TAPE = "0199a000000070008000000000000002";
const FIGURES = "0199a000000070008000000000000003";

/** What flyback-site export-site wrote of a database the .NET site filled (ExportSiteTests), loaded as wrangler would. */
beforeEach(async () => {
  const statements = rows.split("\n").filter((line) => line.trim().length > 0);
  await env.DB.batch(statements.map((sql) => env.DB.prepare(sql)));
});

describe("the .NET site's rows, moved", () => {
  it("list the presets as they were, with what the render made", async () => {
    const listed = await askJson("/api/v1/presets");

    expect(listed.total).toBe(1);
    expect(listed.items[0]).toMatchObject({
      id: LANTERN,
      name: "Łódź lantern",
      author: "Ada",
      description: 'A glow; it says "hi" --twice.',
      tags: ["ambient", "glow"],
      fileName: "Lantern.fbk",
      downloads: 7,
      submitted: "2026-09-01T10:00:00.0000000Z",
      rating: { average: 4, count: 1 },
      lacks: { said: "Needs the Lantern plugin" },
      media: { still: `/media/${LANTERN}.webp`, loop: null, audio: null, peaks: [0.5, 1], state: "done" },
    });
  });

  it("are found by words as before", async () => {
    expect((await askJson("/api/v1/presets?q=" + encodeURIComponent("łódź"))).total).toBe(1);
  });

  it("keep an unpublished preset for the admin, with its failed render", async () => {
    expect((await ask(`/api/v1/presets/${TAPE}`)).status).toBe(404);

    const tape = await (await admin(`/api/v1/presets/${TAPE}`)).json<{ media: { state: string } }>();
    expect(tape.media.state).toBe("failed");
  });

  it("keep each visitor's rating theirs, under the key it was kept with", async () => {
    expect((await askJson(`/api/v1/presets/${LANTERN}/rating`, { from: "203.0.113.1" })).mine).toBe(4);
    expect((await askJson(`/api/v1/presets/${LANTERN}/rating`, { from: "203.0.113.2" })).mine).toBeNull();
  });

  it("list the plugins as they were", async () => {
    const listed = await askJson("/api/v1/plugins?platform=linux&module=flyback.figures.circle&q=shapes");

    expect(listed.items[0]).toMatchObject({
      id: FIGURES,
      assembly: "Flyback.Plugins.Figures",
      builds: ["win", "linux"],
      contract: { "Flyback.Core": "1.2.0", "Flyback.Plugins": "1.2.0" },
      modules: [{ id: "flyback.figures.circle", name: "Circle" }],
      signer: "SIGNER-FINGERPRINT",
      preview: `/api/v1/plugins/${FIGURES}/preview`,
      downloads: 3,
    });
  });

  it("keep the reports and letters for the admin", async () => {
    expect(await (await admin("/api/v1/admin/reports")).json()).toEqual([
      { id: "r1", kind: "preset", subject: LANTERN, name: "Łódź lantern", reason: "broken", details: "No sound.", submitted: "2026-09-04T10:00:00.0000000Z" },
    ]);

    const letters = await (await admin("/api/v1/admin/letters")).json<{ message: string }[]>();
    expect(letters[0]!.message).toBe("Two lines:\nthe second; with a quote ' in it.");
  });

  it("keep a default's file, so the same file again changes nothing", async () => {
    expect(await env.DB.prepare("SELECT preset_id FROM defaults WHERE file_name = 'Lantern.fbk'").first("preset_id")).toBe(LANTERN);
  });

  it("answer the listings the editor's clients are tested against", async () => {
    expect(await askJson("/api/v1/presets")).toEqual(presets);
    expect(await askJson("/api/v1/plugins")).toEqual(plugins);
  });
});
