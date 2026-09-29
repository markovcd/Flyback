// The sound half of the viewer, on a thread of its own: opens the patch's sound in a
// runtime of its own, keeps the speaker's queue topped up, and tells the page what the
// picture needs to know of the sound.
//
// From the page: { open, width, height, opened }, { speaker: port }, { seek, generation },
// { run }, { turn, value }, { strike, down } and { release }. To the page: { ready }, { opened, error, status, speed },
// { opened, state, status } and { failure }. To the speaker: { clear }, { generation, samples } and { trim, generation }.
//
// From the editor instead of { open }: { edit, aspect }, of which only the latest is kept,
// { keep, bytes }, { forget }, { play, values }, { watch, meters } and { aspect }. To the
// editor: { edited, error, status } and { readings, meters, status }.

import { dotnet } from './_framework/dotnet.js';
import * as program from './program.js';

/** Frames rendered per call, and how far ahead of the speaker the queue is kept. */
const CHUNK = 1024;
const AHEAD = 0.25;

/** The queue for a patch played on the computer keyboard: short, so a key is heard as it goes down. */
const PLAYED_AHEAD = 0.1;

/**
 * Seconds of sound rendered and thrown away while the page waits to play, so the engine
 * has optimized the script before anybody hears it. The first play seeks, which forgets them.
 */
const WARM_UP = 3;

/** How much of the old program's sound an edit lets play while the new one starts, in seconds. */
const KEPT_THROUGH_EDIT = 0.1;

/** How long a trim waits on the speaker's answer before the sound carries on without it, in milliseconds. */
const TRIM_PATIENCE = 500;

/** The least time between two states sent to the page, and between two statuses. */
const STATE_EVERY = 15;
const STATUS_EVERY = 200;

const starting = (async () => {
  const runtime = await dotnet.create();
  runtime.setModuleImports('program', program);
  program.attach({
    f32: () => runtime.localHeapViewF32(),
    f64: () => runtime.localHeapViewF64(),
    i32: () => runtime.localHeapViewI32(),
    u8: () => runtime.localHeapViewU8(),
  });

  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  return { runtime, flyback: exports.Flyback.Web.WebExports };
})();

let runtime = null;
let flyback = null;

let speaker = null;
let rate = 48000;
let stateLength = 0;
let opened = 0;
let generation = 0;
let sent = 0;
let played = 0;
let running = false;
let warm = 0;
let ahead = AHEAD;
let toldAt = 0;
let statusAt = 0;

/** How long the last packing of the picture's state took, in milliseconds. */
let listening = 0;

/** Where the speaker's count starts, in seconds: the last seek. */
let origin = 0;

/** Whether the editor drives this worker, by edits and Meters named, rather than a viewer by opens. */
let editing = false;

/** The latest edit not yet taken, when the trim of the sound before it started, and whether one waits on the next run. */
let edit = null;
let trimmedAt = 0;
let stale = false;

/** Every value the editor has played, played again into each program an edit makes. */
const written = new Map();

/** Which Meters the editor last named, and how many. */
let meters = 0;
let watched = 0;

function pump() {
  if (edit !== null && flyback !== null) takeEdit();
  if (!running) warmUp();
  if (!running || speaker === null || flyback === null) return;

  if (trimmedAt > 0) {
    if (performance.now() - trimmedAt < TRIM_PATIENCE) return;
    trimmedAt = 0;
  }

  let rendered = false;

  // A few chunks a call at most, so a message from the page is never kept waiting long.
  for (let i = 0; i < 8 && (sent - played) / rate < ahead; i++) {
    const at = flyback.Hear(CHUNK) / 4;
    const samples = runtime.localHeapViewF32().slice(at, at + CHUNK * 2);
    speaker.postMessage({ generation, samples }, [samples.buffer]);
    sent += CHUNK;
    rendered = true;
  }

  if (rendered) tell(false);
}

/** Renders a chunk nobody hears, a chunk a call, until the script has had its warm-up. */
function warmUp() {
  if (flyback === null || warm <= 0) return;

  flyback.Hear(CHUNK);
  warm -= CHUNK / rate;
}

/** Sends the page the Meters' readings, and now and then how the sound is doing. */
function tell(now) {
  const at = performance.now();
  if (!now && at - toldAt < STATE_EVERY) return;

  toldAt = at;

  if (editing) {
    tellReadings(now, at);
    return;
  }

  const where = flyback.Listen() / 4;
  listening = performance.now() - at;
  const state = where === 0 ? new Float32Array(0) : runtime.localHeapViewF32().slice(where, where + stateLength);
  const message = { opened, state };

  if (now || at - statusAt >= STATUS_EVERY) {
    statusAt = at;
    message.status = status();
  }

  postMessage(message, [state.buffer]);
}

/** The Meters the editor named, measured, in the order it named them. */
function tellReadings(now, at) {
  const where = flyback.Readings() / 4;
  const readings = where === 0 ? new Float32Array(0) : runtime.localHeapViewF32().slice(where, where + watched);
  const message = { readings, meters };

  if (now || at - statusAt >= STATUS_EVERY) {
    statusAt = at;
    message.status = { ...JSON.parse(flyback.Status()), queued: (sent - played) / rate };
  }

  postMessage(message, [readings.buffer]);
}

/** Takes the latest edit, and lets what the old program queued play only while the new one starts. */
function takeEdit() {
  const { edit: text, aspect } = edit;
  edit = null;

  const error = flyback.Edit(text, aspect);

  if (!error) {
    for (const [key, value] of written) flyback.Play(key, value);
    rate = JSON.parse(flyback.Status()).sampleRate;
    cut();
  }

  postMessage({ edited: true, error, status: JSON.parse(flyback.Status()) });
}

/** Asks the speaker to keep only a moment of what is queued; the answer says where the new program starts. */
function cut() {
  if (speaker === null || sent === 0) return;

  if (!running) {
    stale = true;
    return;
  }

  trimmedAt = performance.now();
  speaker.postMessage({ trim: Math.round(KEPT_THROUGH_EDIT * rate), generation });
}

function status() {
  return { ...JSON.parse(flyback.Status()), queued: (sent - played) / rate, listening, knobs: JSON.parse(flyback.Knobs()) };
}

function report({ data }) {
  if (data.generation !== generation) return;

  if (data.trimmed !== undefined) {
    trimmedAt = 0;
    sent = data.trimmed;
    flyback.Carry(origin + sent / rate);
  } else {
    played = data.played;
  }

  pump();
}

function open({ open: what, width, height, opened: id }) {
  running = false;
  opened = id;

  const error = flyback.OpenFile(what.file, what.bytes, width, height, 'sound');

  if (error) {
    postMessage({ opened, error });
    return;
  }

  const opening = status();
  rate = opening.sampleRate;
  stateLength = opening.stateLength;
  ahead = opening.played ? PLAYED_AHEAD : AHEAD;
  warm = opening.soundBackend === 'javascript' ? WARM_UP : 0;

  // The interpreter is as fast on opening as it will ever be, so it can be judged at once.
  const speed = opening.soundBackend === 'interpreter' ? flyback.Measure(0.2) : null;

  postMessage({ opened, error: null, status: opening, speed });
}

function seek({ seek: seconds, generation: next }) {
  flyback.Seek(seconds);
  generation = next;
  origin = seconds;
  sent = played = 0;
  trimmedAt = 0;
  stale = false;
  speaker?.postMessage({ clear: generation });
  tell(true);
  pump();
}

function handle(data) {
  if (data.open !== undefined) open(data);
  else if (data.speaker !== undefined) {
    speaker = data.speaker;
    speaker.onmessage = report;
    speaker.postMessage({ clear: generation });
  } else if (data.seek !== undefined) seek(data);
  else if (data.run !== undefined) {
    running = data.run;
    if (running) warm = 0;

    if (running && stale) {
      stale = false;
      cut();
    }

    pump();
  } else if (data.edit !== undefined) {
    editing = true;
    warm = 0;
    edit = data;
  } else if (data.keep !== undefined) flyback.Keep(data.keep, data.bytes);
  else if (data.forget !== undefined) flyback.Forget();
  else if (data.play !== undefined) {
    data.play.forEach((key, i) => {
      written.set(key, data.values[i]);
      flyback.Play(key, data.values[i]);
    });
  } else if (data.watch !== undefined) {
    flyback.Watch(data.watch);
    meters = data.meters;
    watched = data.watch.length;
  } else if (data.aspect !== undefined) flyback.Aspect(data.aspect); else if (data.turn !== undefined) {
    flyback.Turn(data.turn, data.value);
    if (!running) tell(true);
  } else if (data.strike !== undefined) {
    flyback.Strike(data.strike, data.down);
    tell(true);
  } else if (data.release !== undefined) {
    flyback.Release();
    tell(true);
  }
}

// Set before the runtime has started, so nothing the page sends meanwhile is lost; each waits its turn.
onmessage = async ({ data }) => {
  await starting;

  try {
    handle(data);
  } catch (failure) {
    postMessage({ failure: String(failure?.message ?? failure) });
  }
};

try {
  ({ runtime, flyback } = await starting);
  setInterval(pump, 10);
  postMessage({ ready: true });
} catch (failure) {
  postMessage({ failure: String(failure?.message ?? failure) });
}
