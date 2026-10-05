import { createScheduledController, env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import worker from "../src/index";
import { admin, ask, askJson, outbound, PATCH, published, submit } from "./site";

describe("Validate", () => {
  it("lists what waits, oldest first, and fetches each file whatever its state", async () => {
    const first = await (await submit("First.fbk", PATCH, "First")).json<{ id: string }>();
    await submit("Second.fbk");

    const waiting = await (await admin("/api/v1/admin/unchecked")).json<{ presets: { id: string; name: string; fileName: string }[] }>();

    expect(waiting.presets.map((p) => p.name)).toEqual(["First", "Second"]);
    expect(waiting.presets[0]).toEqual({ id: first.id, name: "First", fileName: "First.fbk" });

    const file = await admin(`/api/v1/admin/presets/${first.id}/file`);
    expect(new TextDecoder().decode(await file.arrayBuffer())).toBe(PATCH);
  });

  it("is started by every submission", async () => {
    outbound.mockClear();

    await ask("/api/v1/presets", {
      method: "POST",
      body: (() => {
        const data = new FormData();
        data.set("file", new File([PATCH], "Drone.fbk"));
        return data;
      })(),
      vars: { GITHUB_DISPATCH_TOKEN: "test-token" },
    });

    const dispatched = outbound.mock.calls.map(([input, init]) => new Request(input, init));
    const started = dispatched.find((r) => r.url.includes("api.github.com"));

    expect(started?.url).toBe("https://api.github.com/repos/markovcd/Flyback/actions/workflows/validate.yml/dispatches");
    expect(started?.headers.get("Authorization")).toBe("Bearer test-token");
    expect(await started?.json()).toEqual({ ref: "main" });
  });

  it("checks a submission once", async () => {
    const { id } = await (await submit()).json<{ id: string }>();
    const said = { accepted: true, name: "Drone", author: null, description: null, tags: [] };

    expect((await admin(`/api/v1/admin/presets/${id}/check`, "PUT", said)).status).toBe(200);
    expect((await admin(`/api/v1/admin/presets/${id}/check`, "PUT", said)).status).toBe(409);
    expect((await admin("/api/v1/admin/presets/nothing-here/check", "PUT", said)).status).toBe(404);
  });

  it("refuses a check that does not say what it should", async () => {
    const { id } = await (await submit()).json<{ id: string }>();

    expect((await admin(`/api/v1/admin/presets/${id}/check`, "PUT", { accepted: false })).status).toBe(400);
    expect((await admin(`/api/v1/admin/presets/${id}/check`, "PUT", { accepted: true, name: "" })).status).toBe(400);
    expect((await admin(`/api/v1/admin/presets/${id}/check`, "PUT", { accepted: true, name: "Drone", tags: "drone" })).status).toBe(400);
    expect(
      (await admin(`/api/v1/admin/presets/${id}/check`, "PUT", { accepted: true, name: "Drone", lacks: { plugins: [], modules: -1, said: "" } }))
        .status,
    ).toBe(400);

    expect((await askJson(`/api/v1/presets/${id}`)).status).toBe("unchecked");
  });

  it("refreshes what the web pages lack, for every checked preset", async () => {
    const id = await published("Drone");
    await submit("Waiting.fbk");

    const listed = await (await admin("/api/v1/admin/presets")).json<{ items: { id: string }[] }>();
    expect(listed.items.map((i) => i.id)).toEqual([id]);

    const lacks = { plugins: [{ id: "example.kite", name: "Kite" }], modules: 2, said: "Needs the Kite plugin" };
    expect((await admin(`/api/v1/admin/presets/${id}/lacks`, "PUT", lacks)).status).toBe(204);
    expect((await askJson(`/api/v1/presets/${id}`)).lacks).toEqual(lacks);

    expect((await admin(`/api/v1/admin/presets/${id}/lacks`, "PUT", null)).status).toBe(204);
    expect((await askJson(`/api/v1/presets/${id}`)).lacks).toBeNull();
  });
});

describe("the hourly sweep", () => {
  it("forgets a refusal a week after it was sent, file and all", async () => {
    const { id: old } = await (await submit("Old.fbk")).json<{ id: string }>();
    const { id: recent } = await (await submit("Recent.fbk")).json<{ id: string }>();

    for (const id of [old, recent]) await admin(`/api/v1/admin/presets/${id}/check`, "PUT", { accepted: false, reason: "Not a patch." });
    await env.DB.prepare("UPDATE presets SET submitted_at = '2020-01-01T00:00:00.0000000Z' WHERE id = ?").bind(old).run();

    await worker.scheduled(createScheduledController(), env);

    expect((await ask(`/api/v1/presets/${old}`)).status).toBe(404);
    expect(await env.FILES.head(`presets/${old}`)).toBeNull();
    expect((await askJson(`/api/v1/presets/${recent}`)).status).toBe("refused");
  });

  it("forgets rate-limit windows that have closed", async () => {
    await env.DB.prepare("INSERT INTO limits (visitor, policy, window, count) VALUES ('x', 'submit', 0, 3)").run();

    await worker.scheduled(createScheduledController(), env);

    expect(await env.DB.prepare("SELECT count(*) AS n FROM limits").first("n")).toBe(0);
  });
});
