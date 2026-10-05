import type { Env } from "./env";
import { body, json, noContent, notFound, refused, text } from "./http";
import { limited } from "./limits";
import { clip, newId, now } from "./text";

/** What a letter may be about, as the editor offers them. */
const MOODS = ["good", "bad", "idea", "other"];

const MESSAGE_LIMIT = 2000;
const CONTACT_LIMIT = 200;

/** What the editor may say about itself, per field, before it is cut short. */
const ABOUT_LIMIT = 600;

const said = (value: string | null): string | null => {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
};

/**
 * What the editor said about itself, kept but capped. It is filled in by the program
 * rather than typed, so too much of it is a fault rather than a refusal.
 */
const cut = (value: string | null): string | null => {
  const kept = said(value);
  return kept === null ? null : clip(kept, ABOUT_LIMIT);
};

export async function writeLetter(env: Env, request: Request): Promise<Response> {
  const turnedAway = await limited(env, request, "letter");
  if (turnedAway) return turnedAway;

  const form = await body(request);
  if (form === null) return refused(400, "Send the letter as JSON.");

  const mood = text(form, "mood");
  if (mood === null || !MOODS.includes(mood)) return refused(400, `A mood is one of ${MOODS.join(", ")}.`);

  const message = text(form, "message")?.trim();
  if (!message) return refused(400, "A letter needs something in it.");
  if (message.length > MESSAGE_LIMIT) return refused(400, `Say it in ${MESSAGE_LIMIT} characters or fewer.`);

  const contact = text(form, "contact");
  if ((contact?.trim().length ?? 0) > CONTACT_LIMIT)
    return refused(400, `An address is ${CONTACT_LIMIT} characters or fewer.`);

  await env.DB.prepare(
    `INSERT INTO letters (id, mood, message, contact, version, platform, plugins, submitted_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
  )
    .bind(
      newId(),
      mood,
      message,
      said(contact),
      cut(text(form, "version")),
      cut(text(form, "platform")),
      cut(text(form, "plugins")),
      now(),
    )
    .run();

  return noContent();
}

/** Every letter, newest first. */
export async function listLetters(env: Env): Promise<Response> {
  const { results } = await env.DB.prepare(
    `SELECT id, mood, message, contact, version, platform, plugins, submitted_at AS submitted
     FROM letters ORDER BY submitted_at DESC, id DESC`,
  ).all();

  return json(results);
}

export async function dismissLetter(env: Env, id: string): Promise<Response> {
  const { meta } = await env.DB.prepare("DELETE FROM letters WHERE id = ?").bind(id).run();
  return meta.changes > 0 ? noContent() : notFound();
}
