/** JSON answers shaped as the .NET server gave them: camelCase, `{ error }` on a refusal. */

export const json = (body: unknown, status = 200, headers?: HeadersInit): Response =>
  Response.json(body, { status, headers });

export const refused = (status: number, error: string): Response => json({ error }, status);

export const badRequest = (error: string): Response => refused(400, error);

export const notFound = (): Response => new Response(null, { status: 404 });

export const unauthorized = (): Response => new Response(null, { status: 401 });

export const noContent = (): Response => new Response(null, { status: 204 });

/** The body as a JSON object, or null where it is not one. */
export async function body(request: Request): Promise<Record<string, unknown> | null> {
  try {
    const read: unknown = await request.json();
    return read !== null && typeof read === "object" && !Array.isArray(read) ? (read as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

/** A property whatever its case, as ASP.NET binds one. */
export function field(from: Record<string, unknown>, name: string): unknown {
  if (name in from) return from[name];
  const lower = name.toLowerCase();
  for (const key of Object.keys(from)) if (key.toLowerCase() === lower) return from[key];
  return undefined;
}

export const text = (from: Record<string, unknown>, name: string): string | null => {
  const value = field(from, name);
  return typeof value === "string" ? value : null;
};

/** A query flag the way ASP.NET binds a bool?: absent is null, anything but true or false a 400. */
export function flag(url: URL, name: string): boolean | null | "bad" {
  const value = url.searchParams.get(name);
  if (value === null || value === "") return null;
  const lower = value.toLowerCase();
  return lower === "true" ? true : lower === "false" ? false : "bad";
}

/** A query number the way ASP.NET binds an int?: absent is null, anything but a whole number a 400. */
export function whole(url: URL, name: string): number | null | "bad" {
  const value = url.searchParams.get(name);
  if (value === null || value === "") return null;
  if (!/^[+-]?\d{1,10}$/.test(value.trim())) return "bad";
  const number = Number(value.trim());
  return number > 2147483647 || number < -2147483648 ? "bad" : number;
}

/** A Content-Disposition that names the file for a download, as Results.File writes it. */
export function attachment(fileName: string): string {
  const plain = fileName.replace(/[^\x20-\x7e]|["\\]/g, "_");
  return `attachment; filename="${plain}"; filename*=UTF-8''${encodeURIComponent(fileName)}`;
}
