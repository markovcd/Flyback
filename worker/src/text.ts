/** Text as the site keeps it: tidied like Patch.Tidied, clipped like TextLimit.Clip, folded for search. */

/** What char.IsWhiteSpace counts, which is what string.Split(null) splits on. */
const SPACE = /[\t-\r \u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+/;

/** Never ends on half a surrogate pair. */
export function clip(text: string, limit: number): string {
  if (text.length <= limit) return text;
  const code = limit > 0 ? text.charCodeAt(limit - 1) : 0;
  return limit > 0 && code >= 0xd800 && code <= 0xdbff ? text.slice(0, limit - 1) : text.slice(0, Math.max(limit, 0));
}

/** One line, single spaces, straight quotes curled, at most limit characters; null where blank. */
export function tidied(text: string | null | undefined, limit: number): string | null {
  if (text == null) return null;
  const words = text.split(SPACE).filter((w) => w.length > 0);
  if (words.length === 0) return null;

  let opening = true;
  const kept = words
    .map((word) => word.replace(/"/g, () => ((opening = !opening) ? "\u201d" : "\u201c")))
    .join(" ");

  return kept.length > limit ? clip(kept, limit).trimEnd() : kept;
}

export const NAME_LIMIT = 60;

/** Patch.DescriptionLimit, which a name is tidied to before it is clipped to NAME_LIMIT. */
const DESCRIPTION_LIMIT = 400;

/** A preset name as Submissions.Named holds it: one line, NAME_LIMIT characters, null where blank. */
export function named(name: string | null | undefined): string | null {
  const called = tidied(name, DESCRIPTION_LIMIT);
  return called !== null && called.length > NAME_LIMIT ? clip(called, NAME_LIMIT).trimEnd() : called;
}

/** What a search matches against: every part lowercased, kept apart so no word spans two. */
export const folded = (...parts: (string | null | undefined)[]): string =>
  parts.map((p) => (p ?? "").toLowerCase()).join("\u001f");

/** The words of a search, at most eight. */
export const words = (search: string | null): string[] =>
  (search ?? "").split(" ").map((w) => w.trim()).filter((w) => w.length > 0).slice(0, 8);

/** A word to find anywhere, with LIKE's own characters escaped by a backslash. */
export const anywhere = (word: string): string =>
  "%" + word.toLowerCase().replace(/[\\%_]/g, (c) => "\\" + c) + "%";

export const SEPARATOR = "\u001f";
export const PAIR = "\u001e";

export const joined = (values: readonly string[]): string => values.join(SEPARATOR);

export const split = (value: string | null): string[] => (value ? value.split(SEPARATOR) : []);

/** Now, as .NET's round-trip format writes a UTC time, so old and new rows sort together. */
export function now(at = new Date()): string {
  return at.toISOString().replace(/\.(\d{3})Z$/, ".$10000Z");
}

/** A version 7 UUID without dashes, as Guid.CreateVersion7().ToString("N") writes one. */
export function newId(at = Date.now()): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  let time = at;
  for (let i = 5; i >= 0; i--) {
    bytes[i] = time % 256;
    time = Math.floor(time / 256);
  }
  bytes[6] = (bytes[6]! & 0x0f) | 0x70;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  return hex(bytes);
}

export const hex = (bytes: Uint8Array): string => Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");

export async function sha256(bytes: ArrayBuffer | Uint8Array): Promise<string> {
  return hex(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)));
}

/** The last segment of a path a browser sent as a file's name. */
export const baseName = (path: string): string => path.split(/[\\/]/).pop() ?? "";

export function extension(fileName: string): string {
  const dot = fileName.lastIndexOf(".");
  return dot < 0 ? "" : fileName.slice(dot).toLowerCase();
}

export function stem(fileName: string): string {
  const dot = fileName.lastIndexOf(".");
  return dot < 0 ? fileName : fileName.slice(0, dot);
}
