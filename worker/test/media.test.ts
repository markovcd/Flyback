import { describe, expect, it } from "vitest";
import { admin, ask, askJson, asAdmin, published } from "./site";

const upload = async (id: string, name: string, bytes: BodyInit) =>
  ask(`/api/v1/admin/presets/${id}/media/${name}`, { method: "PUT", headers: await asAdmin(), body: bytes });

describe("a preset's render", () => {
  it("shows once render-presets has uploaded it, done last", async () => {
    const id = await published("Drone");

    expect((await askJson(`/api/v1/presets/${id}`)).media).toEqual({ still: null, loop: null, audio: null, peaks: null, state: "pending" });

    expect((await upload(id, "webp", new Uint8Array([1, 2, 3]))).status).toBe(204);
    expect((await upload(id, "peaks.json", "[0.5, 1]")).status).toBe(204);
    expect((await upload(id, "done", "")).status).toBe(204);

    expect((await askJson(`/api/v1/presets/${id}`)).media).toEqual({
      still: `/media/${id}.webp?v=2`,
      loop: null,
      audio: null,
      peaks: [0.5, 1],
      state: "done",
    });

    const still = await ask(`/media/${id}.webp`);
    expect(new Uint8Array(await still.arrayBuffer())).toEqual(new Uint8Array([1, 2, 3]));
    expect(still.headers.get("Content-Type")).toBe("image/webp");
    expect(still.headers.get("Cache-Control")).toBe("no-cache");

    const kept = await ask(`/media/${id}.webp?v=2`);
    expect(kept.headers.get("Cache-Control")).toBe("public, max-age=31536000, immutable");
    await kept.arrayBuffer();
  });

  it("gets a new URL each time its files change, so an old one never serves a new render", async () => {
    const id = await published("Drone");
    await upload(id, "webp", new Uint8Array([1]));
    const first = (await askJson(`/api/v1/presets/${id}`)).media.still;

    await upload(id, "webp", new Uint8Array([2]));
    const second = (await askJson(`/api/v1/presets/${id}`)).media.still;
    expect(second).not.toBe(first);

    await admin(`/api/v1/admin/presets/${id}/media`, "DELETE");
    await upload(id, "webp", new Uint8Array([3]));
    expect((await askJson(`/api/v1/presets/${id}`)).media.still).not.toBe(second);
  });

  it("stops the preset waiting once done or failed", async () => {
    const done = await published("Done");
    const failed = await published("Failed");
    const waiting = await published("Waiting");

    await upload(done, "done", "");
    await upload(failed, "failed", "no plugin");

    expect((await askJson("/api/v1/presets?pending=true")).items.map((i: { id: string }) => i.id)).toEqual([waiting]);
    expect((await askJson(`/api/v1/presets/${failed}`)).media.state).toBe("failed");
  });

  it("is rendered again once it is cleared", async () => {
    const id = await published("Drone");
    await upload(id, "webp", new Uint8Array([1]));
    await upload(id, "done", "");

    expect((await admin(`/api/v1/admin/presets/${id}/media`, "DELETE")).status).toBe(204);

    expect((await askJson("/api/v1/presets?pending=true")).total).toBe(1);
    expect((await ask(`/media/${id}.webp`)).status).toBe(404);
  });

  it("takes only the names render-presets makes", async () => {
    const id = await published("Drone");

    expect((await upload(id, "index.html", "<script>")).status).toBe(400);
    expect((await upload(id, "..%2F..%2Fpresets%2Fx", "x")).status).toBe(400);
    expect((await upload(id, "peaks.json", "{}")).status).toBe(400);
    expect((await upload("nothing-here", "webp", "x")).status).toBe(404);
    expect((await ask(`/api/v1/admin/presets/${id}/media/webp`, { method: "PUT", body: "x" })).status).toBe(401);
  });

  it("serves no marker, and nothing that is not a render", async () => {
    const id = await published("Drone");
    await upload(id, "failed", "stack trace");

    expect((await ask(`/media/${id}.failed`)).status).toBe(404);
    expect((await ask(`/media/${id}.done`)).status).toBe(404);
    expect((await ask(`/media/../presets/${id}`)).status).toBe(404);
  });

  it("is served a range at a time for the player, and checked before each use", async () => {
    const id = await published("Drone");
    await upload(id, "mp3", new Uint8Array([0, 1, 2, 3, 4, 5, 6, 7]));

    const part = await ask(`/media/${id}.mp3`, { headers: { Range: "bytes=2-4" } });
    expect(part.status).toBe(206);
    expect(part.headers.get("Content-Range")).toBe("bytes 2-4/8");
    expect(new Uint8Array(await part.arrayBuffer())).toEqual(new Uint8Array([2, 3, 4]));

    const whole = await ask(`/media/${id}.mp3`);
    const etag = whole.headers.get("ETag")!;
    await whole.arrayBuffer();

    expect((await ask(`/media/${id}.mp3`, { headers: { "If-None-Match": etag } })).status).toBe(304);
  });

  it("goes with the preset it is of", async () => {
    const id = await published("Drone");
    await upload(id, "webp", new Uint8Array([1]));

    await admin(`/api/v1/admin/presets/${id}`, "DELETE");

    expect((await ask(`/media/${id}.webp`)).status).toBe(404);
  });
});
