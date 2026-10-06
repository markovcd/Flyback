import { describe, expect, it } from "vitest";
import { admin, ask, askJson, published } from "./site";

const kept = { READ_CACHE_SECONDS: "60" };

describe("the anonymous reads", () => {
  it("are kept for a window, so a crowd costs one database read", async () => {
    const id = await published("Drone");
    const first = await ask("/api/v1/presets", { vars: kept });
    expect(first.headers.get("Cache-Control")).toBe("public, max-age=60");
    expect((await first.json<{ total: number }>()).total).toBe(1);

    await admin(`/api/v1/admin/presets/${id}`, "DELETE");

    expect((await askJson("/api/v1/presets", { vars: kept })).total).toBe(1);
    expect((await askJson("/api/v1/presets")).total).toBe(0);
  });

  it("are kept nowhere unless a window is set", async () => {
    await published("Drone");
    expect((await ask("/api/v1/presets")).headers.get("Cache-Control")).toBeNull();
  });

  it("never include the admin's view, the render queue, a file or a rating", async () => {
    const id = await published("Drone");

    for (const path of ["/api/v1/presets?pending=true", `/api/v1/presets/${id}/file`, `/api/v1/presets/${id}/rating`]) {
      const response = await ask(path, { vars: kept });
      expect(response.headers.get("Cache-Control"), path).not.toBe("public, max-age=60");
      await response.arrayBuffer();
    }

    const asAdmin = await admin("/api/v1/presets");
    expect(asAdmin.headers.get("Cache-Control")).not.toBe("public, max-age=60");
  });
});
