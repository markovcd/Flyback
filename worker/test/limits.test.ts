import { describe, expect, it } from "vitest";
import { ask, PATCH, form } from "./site";

const post = (from: string) =>
  ask("/api/v1/presets", { method: "POST", body: form("Drone.fbk", PATCH), from, vars: { POSTS_PER_HOUR: "2" } });

const write = (from: string) =>
  ask("/api/v1/letters", {
    method: "POST",
    from,
    vars: { LETTERS_PER_HOUR: "2" },
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ mood: "good", message: "Hello." }),
  });

describe("a flood", () => {
  it("of submissions is turned away", async () => {
    expect((await post("203.0.113.7")).status).toBe(202);
    expect((await post("203.0.113.7")).status).toBe(202);
    expect((await post("203.0.113.7")).status).toBe(429);
  });

  it("is counted by the address Cloudflare saw, whatever the request says of itself", async () => {
    const claiming = (claim: string) =>
      ask("/api/v1/presets", {
        method: "POST",
        body: form("Drone.fbk", PATCH),
        from: "203.0.113.8",
        vars: { POSTS_PER_HOUR: "2" },
        headers: { "X-Forwarded-For": claim },
      });

    await claiming("198.51.100.1");
    await claiming("198.51.100.2");
    expect((await claiming("198.51.100.3")).status).toBe(429);
  });

  it("from one visitor leaves another's allowance alone", async () => {
    await post("203.0.113.9");
    await post("203.0.113.9");
    expect((await post("203.0.113.10")).status).toBe(202);
  });

  it("from an IPv6 household is one allowance however many addresses it takes", async () => {
    await post("2001:db8:1:2::1");
    await post("2001:db8:1:2::2");
    expect((await post("2001:db8:1:2:aaaa:bbbb:cccc:dddd")).status).toBe(429);
    expect((await post("2001:db8:1:3::1")).status).toBe(202);
  });

  it("of letters is turned away", async () => {
    expect((await write("203.0.113.11")).status).toBe(204);
    expect((await write("203.0.113.11")).status).toBe(204);
    expect((await write("203.0.113.11")).status).toBe(429);
  });
});
