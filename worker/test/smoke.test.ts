import { describe, expect, it } from "vitest";
import { ask, askJson } from "./site";

describe("the site", () => {
  it("lists nothing when nothing is shared", async () => {
    const page = await askJson("/api/v1/presets");
    expect(page).toEqual({ items: [], total: 0, page: 1, pageSize: 24 });
  });

  it("serves the front page at the root", async () => {
    const response = await ask("/");
    expect(response.status).toBe(200);
    expect(await response.text()).toContain("The front page");
  });
});
