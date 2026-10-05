import { env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import { admin, ask, askJson, freshAddress, published } from "./site";

const sendJson = (path: string, method: string, sent: unknown, headers: Record<string, string> = {}, from?: string) =>
  ask(path, { method, from, headers: { "Content-Type": "application/json", ...headers }, body: JSON.stringify(sent) });

const reportOn = (id: string, sent: unknown) => sendJson(`/api/v1/presets/${id}/reports`, "POST", sent);

const rate = (id: string, sent: unknown, from = "203.0.113.1", fromTheSite = true) =>
  sendJson(`/api/v1/presets/${id}/rating`, "PUT", sent, fromTheSite ? { "Sec-Fetch-Site": "same-origin" } : {}, from);

const write = (letter: unknown) => sendJson("/api/v1/letters", "POST", letter, {}, freshAddress());

describe("reports", () => {
  it("are made by anyone and read only by the admin", async () => {
    const id = await published("Drone");

    expect((await reportOn(id, { reason: "offensive", details: "  Look at the picture.  " })).status).toBe(204);
    expect((await reportOn(id, { reason: "broken" })).status).toBe(204);

    expect((await ask("/api/v1/admin/reports")).status).toBe(401);

    const reports = await (await admin("/api/v1/admin/reports")).json<Record<string, unknown>[]>();

    expect(reports.map((r) => r.reason)).toEqual(["broken", "offensive"]);
    expect(reports.every((r) => r.kind === "preset" && r.name === "Drone" && r.subject === id)).toBe(true);
    expect(reports[1]!.details).toBe("Look at the picture.");
    expect(reports[0]!.details).toBeNull();

    expect((await ask(`/api/v1/admin/reports/${reports[0]!.id}`, { method: "DELETE" })).status).toBe(401);
    expect((await admin(`/api/v1/admin/reports/${reports[0]!.id}`, "DELETE")).status).toBe(204);
    expect(await (await admin("/api/v1/admin/reports")).json()).toHaveLength(1);
  });

  it("need a known reason and a preset that is there to see", async () => {
    const id = await published("Drone");

    expect((await reportOn(id, { reason: "boring" })).status).toBe(400);
    expect((await reportOn(id, { details: "No reason given." })).status).toBe(400);
    expect((await reportOn(id, { reason: "other", details: "x".repeat(1001) })).status).toBe(400);
    expect((await reportOn("nothing-here", { reason: "other" })).status).toBe(404);

    await admin(`/api/v1/admin/presets/${id}`, "PATCH", { published: false });
    expect((await reportOn(id, { reason: "other" })).status).toBe(404);
  });

  it("go with the preset they are about", async () => {
    const id = await published("Drone");
    await reportOn(id, { reason: "stolen" });

    await admin(`/api/v1/admin/presets/${id}`, "DELETE");

    expect(await (await admin("/api/v1/admin/reports")).json()).toEqual([]);
  });
});

describe("ratings", () => {
  it("are one per address, and listed as their average", async () => {
    const id = await published("Drone");

    expect((await rate(id, { stars: 2 }, "203.0.113.1")).status).toBe(200);
    expect((await rate(id, { stars: 5 }, "203.0.113.2")).status).toBe(200);
    const again = await (await rate(id, { stars: 4 }, "203.0.113.1")).json();

    expect(again).toEqual({ average: 4.5, count: 2, mine: 4 });
    expect((await askJson("/api/v1/presets")).items[0].rating).toEqual({ average: 4.5, count: 2 });
    expect((await askJson(`/api/v1/presets/${id}`)).rating.count).toBe(2);
  });

  it("round to hundredths with a half going to the even neighbor", async () => {
    const id = await published("Drone");
    const given = [5, 5, 4, 4, 4, 4, 4, 3];

    for (const [i, stars] of given.entries()) await rate(id, { stars }, `203.0.113.${20 + i}`);

    expect((await askJson(`/api/v1/presets/${id}/rating`)).average).toBe(4.12);
  });

  it("are one per IPv6 household however many addresses it takes", async () => {
    const id = await published("Drone");

    await rate(id, { stars: 2 }, "2001:db8:1:2::1");
    expect(await (await rate(id, { stars: 5 }, "2001:db8:1:2::ffff")).json()).toMatchObject({ count: 1, mine: 5 });
  });

  it("say so where nobody rated", async () => {
    const id = await published("Drone");

    expect(await askJson(`/api/v1/presets/${id}/rating`)).toEqual({ average: 0, count: 0, mine: null });
  });

  it("are taken only from the site, and only one to five stars", async () => {
    const id = await published("Drone");

    expect((await rate(id, { stars: 5 }, undefined, false)).status).toBe(403);
    expect((await rate(id, { stars: 0 })).status).toBe(400);
    expect((await rate(id, { stars: 6 })).status).toBe(400);
    expect((await rate(id, { stars: 2.5 })).status).toBe(400);
    expect((await rate(id, {})).status).toBe(400);
    expect((await rate("nothing-here", { stars: 3 })).status).toBe(404);

    await admin(`/api/v1/admin/presets/${id}`, "PATCH", { published: false });
    expect((await rate(id, { stars: 3 })).status).toBe(404);
  });

  it("go with the preset they are of", async () => {
    const id = await published("Drone");
    await rate(id, { stars: 1 });

    await admin(`/api/v1/admin/presets/${id}`, "DELETE");

    expect(await env.DB.prepare("SELECT count(*) AS n FROM ratings").first("n")).toBe(0);
  });

  it("keep who rated out of the table", async () => {
    const id = await published("Drone");
    await rate(id, { stars: 3 }, "203.0.113.99");

    const voter = await env.DB.prepare("SELECT voter FROM ratings").first<string>("voter");
    expect(voter).toMatch(/^[0-9a-f]{64}$/);
  });
});

describe("letters", () => {
  it("are written by anyone and read only by the admin", async () => {
    expect(
      (
        await write({
          mood: "good",
          message: "  The canvas is lovely.  ",
          contact: "  ada@example.org  ",
          version: "1.4.0",
          platform: "Microsoft Windows 10.0.26200",
          plugins: "WinIO, Picture; sound: WASAPI",
        })
      ).status,
    ).toBe(204);
    expect((await write({ mood: "bad", message: "The delay clicks." })).status).toBe(204);

    expect((await ask("/api/v1/admin/letters")).status).toBe(401);

    const letters = await (await admin("/api/v1/admin/letters")).json<Record<string, unknown>[]>();

    expect(letters.map((l) => l.mood)).toEqual(["bad", "good"]);
    expect(letters[1]).toMatchObject({
      message: "The canvas is lovely.",
      contact: "ada@example.org",
      version: "1.4.0",
      plugins: "WinIO, Picture; sound: WASAPI",
    });
    expect(letters[0]).toMatchObject({ contact: null, version: null });

    expect((await admin(`/api/v1/admin/letters/${letters[0]!.id}`, "DELETE")).status).toBe(204);
    expect(await (await admin("/api/v1/admin/letters")).json()).toHaveLength(1);
  });

  it("need a known mood and something in them", async () => {
    expect((await write({ mood: "cross", message: "Hello." })).status).toBe(400);
    expect((await write({ message: "No mood given." })).status).toBe(400);
    expect((await write({ mood: "other" })).status).toBe(400);
    expect((await write({ mood: "other", message: "   " })).status).toBe(400);
    expect((await write({ mood: "other", message: "x".repeat(2001) })).status).toBe(400);
    expect((await write({ mood: "other", message: "Fine.", contact: "x".repeat(201) })).status).toBe(400);
  });

  it("keep what the editor says about itself, cut rather than refused", async () => {
    expect((await write({ mood: "bad", message: "It will not start.", plugins: "p".repeat(900) })).status).toBe(204);

    const letters = await (await admin("/api/v1/admin/letters")).json<{ plugins: string }[]>();
    expect(letters[0]!.plugins).toHaveLength(600);
  });

  it("are taken with the editor's PascalCase too", async () => {
    expect((await write({ Mood: "idea", Message: "Hello." })).status).toBe(204);
  });
});
