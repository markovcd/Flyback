import { describe, expect, it } from "vitest";
import { clip, named, newId, now, tidied } from "../src/text";
import { visitorOf } from "../src/visitor";

const from = (address: string) => visitorOf(new Request("https://flyback.test/", { headers: { "CF-Connecting-IP": address } }));

describe("text", () => {
  it("is tidied as Patch.Tidied tidies it", () => {
    expect(tidied("  a\t\n b  ", 400)).toBe("a b");
    expect(tidied('say "hi" and "bye"', 400)).toBe("say “hi” and “bye”");
    expect(tidied("   ", 400)).toBeNull();
    expect(tidied(" x　y", 400)).toBe("x y");
    expect(tidied("abc def", 5)).toBe("abc d");
    expect(tidied("abc   def", 4)).toBe("abc");
  });

  it("is clipped whole to the character", () => {
    expect(clip("ab\u{1f3b9}", 3)).toBe("ab");
    expect(clip("abc", 5)).toBe("abc");
  });

  it("names a preset in sixty characters", () => {
    expect(named("x".repeat(70))).toHaveLength(60);
    expect(named("  ")).toBeNull();
  });

  it("writes times and ids as the .NET site wrote them", () => {
    expect(now(new Date("2026-10-05T12:34:56.789Z"))).toBe("2026-10-05T12:34:56.7890000Z");
    expect(newId()).toMatch(/^[0-9a-f]{12}7[0-9a-f]{3}[89ab][0-9a-f]{15}$/);
    expect(newId(1) < newId(2)).toBe(true);
  });
});

describe("a visitor", () => {
  it("is the address Cloudflare saw", () => {
    expect(from("203.0.113.5")).toBe("203.0.113.5");
    expect(visitorOf(new Request("https://flyback.test/"))).toBe("unknown");
  });

  it("on IPv6 is the /64, written as .NET writes it", () => {
    expect(from("2001:DB8:1:2:aaaa:bbbb:cccc:dddd")).toBe("2001:db8:1:2::/64");
    expect(from("2001:db8::1")).toBe("2001:db8::/64");
    expect(from("2001:0:0:1::5")).toBe("2001:0:0:1::/64");
    expect(from("::1")).toBe("::/64");
  });

  it("on IPv4 carried in IPv6 is the IPv4 address", () => {
    expect(from("::ffff:192.0.2.1")).toBe("192.0.2.1");
    expect(from("::ffff:c000:201")).toBe("192.0.2.1");
  });
});
