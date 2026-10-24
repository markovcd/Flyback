import type { Env } from "./env";
import { attachment, badRequest, flag, json, noContent, notFound, refused, whole } from "./http";
import { forgetRatings, type Kind, NO_RATING, type Rating, ratingOf, ratingsOf } from "./ratings";
import { forgetReports } from "./reports";
import { anywhere, words } from "./text";

const PAGE_SIZE = 24;

/** What every stored row has: an id, a file, and where it stands with Validate and the admin. */
export interface StoredRow {
  id: string;
  file_name: string;
  published: number;
  status: "unchecked" | "checked" | "refused";
}

/**
 * One kind of submission the site keeps: a table of rows aliased p, a file in R2 for
 * each, and how a row is shown. Listing, finding, downloading and forgetting are the
 * same for every kind; only what is filtered on and what is answered differ.
 */
export interface Store<Row extends StoredRow> {
  kind: Kind;
  table: string;
  /** The columns a row is read with, over the table aliased p. */
  columns: string;
  view: (row: Row, rating: Rating) => unknown;
  /** The R2 key of a row's file. */
  file: (id: string) => string;
  /** Every R2 key a row may own, its file included. */
  owned: (id: string) => string[];
}

/** What anyone may see: checked, and published unless the admin is asking. */
export const visible = (admin: boolean): string => (admin ? "p.status = 'checked'" : "p.status = 'checked' AND p.published = 1");

export const find = <Row extends StoredRow>(store: Store<Row>, env: Env, id: string, where = "1 = 1"): Promise<Row | null> =>
  env.DB.prepare(`SELECT ${store.columns} FROM ${store.table} p WHERE p.id = ? AND ${where}`).bind(id).first<Row>();

/** Whether the row is there for anyone to see, report and rate. */
export const shown = async <Row extends StoredRow>(store: Store<Row>, env: Env, id: string): Promise<boolean> =>
  (await find(store, env, id, visible(false))) !== null;

/** The rows as they are answered, each with its rating. */
export async function views<Row extends StoredRow>(store: Store<Row>, env: Env, rows: Row[]): Promise<unknown[]> {
  const rated = await ratingsOf(env, store.kind, rows.map((r) => r.id));
  return rows.map((r) => store.view(r, rated.get(r.id) ?? NO_RATING));
}

/**
 * A page of what anyone may see, newest first, matching all of q's words and the
 * conditions given, with how many match in all.
 */
export async function page<Row extends StoredRow>(
  store: Store<Row>,
  env: Env,
  url: URL,
  admin: boolean,
  where: string[] = [],
  values: unknown[] = [],
): Promise<Response> {
  const asked = whole(url, "page");
  if (asked === "bad") return badRequest("A page is a whole number.");

  const at = Math.max(1, asked ?? 1);
  const all = [visible(admin), ...where];
  const bound = [...values];

  for (const word of words(url.searchParams.get("q"))) {
    all.push("p.search LIKE ? ESCAPE '\\'");
    bound.push(anywhere(word));
  }

  const filter = "WHERE " + all.join(" AND ");
  const [counted, listed] = await env.DB.batch<{ n: number } | Row>([
    env.DB.prepare(`SELECT count(*) AS n FROM ${store.table} p ${filter}`).bind(...bound),
    env.DB.prepare(
      `SELECT ${store.columns} FROM ${store.table} p ${filter} ORDER BY p.submitted_at DESC, p.id DESC LIMIT ? OFFSET ?`,
    ).bind(...bound, PAGE_SIZE, (at - 1) * PAGE_SIZE),
  ]);

  return json({
    items: await views(store, env, listed!.results as Row[]),
    total: (counted!.results[0] as { n: number }).n,
    page: at,
    pageSize: PAGE_SIZE,
  });
}

/**
 * GET /{kind}s/{id}. One still being checked, or refused, is answered to whoever has
 * its id, which only its submitter does, so they can read why.
 */
export async function one<Row extends StoredRow>(store: Store<Row>, env: Env, id: string, admin: boolean): Promise<Response> {
  const row = await find(store, env, id);
  if (row === null || (row.status === "checked" && row.published === 0 && !admin)) return notFound();
  return json(store.view(row, await ratingOf(env, store.kind, id)));
}

/** GET /{kind}s/{id}/file, counted as a download unless count=false. */
export async function fileOf<Row extends StoredRow>(store: Store<Row>, env: Env, url: URL, id: string, admin: boolean): Promise<Response> {
  const count = flag(url, "count");
  if (count === "bad") return badRequest("count is true or false.");

  const row = await find(store, env, id, visible(admin));
  if (row === null) return notFound();

  const file = await env.FILES.get(store.file(id));
  if (file === null) return notFound();

  if (count !== false) await env.DB.prepare(`UPDATE ${store.table} SET downloads = downloads + 1 WHERE id = ?`).bind(id).run();

  return download(file, row.file_name);
}

export const download = (file: R2ObjectBody, fileName: string): Response =>
  new Response(file.body, {
    headers: {
      "Content-Type": "application/octet-stream",
      "Content-Disposition": attachment(fileName),
      "Content-Length": String(file.size),
    },
  });

/** The form, or the answer that refuses it: not a form, or larger than limit and a megabyte for the rest. */
export async function formOf(request: Request, limit: number, notForm: string): Promise<FormData | Response> {
  const type = request.headers.get("Content-Type") ?? "";
  if (!/^(multipart\/form-data|application\/x-www-form-urlencoded)\b/i.test(type)) return badRequest(notForm);

  const length = Number(request.headers.get("Content-Length") ?? "0");
  if (length > limit + (1 << 20)) return refused(413, "That is too large.");

  try {
    return await request.formData();
  } catch {
    return badRequest(notForm);
  }
}

/** The row, everything it owns in R2, its reports and its ratings; false where there was no row. */
export async function forget<Row extends StoredRow>(store: Store<Row>, env: Env, id: string): Promise<boolean> {
  const { meta } = await env.DB.prepare(`DELETE FROM ${store.table} WHERE id = ?`).bind(id).run();
  if (meta.changes === 0) return false;

  await env.FILES.delete(store.owned(id));
  await forgetReports(env, store.kind, id);
  await forgetRatings(env, store.kind, id);

  return true;
}

/** DELETE /admin/{kind}s/{id}. */
export async function remove<Row extends StoredRow>(store: Store<Row>, env: Env, id: string): Promise<Response> {
  if (!(await forget(store, env, id))) return notFound();
  return noContent();
}
