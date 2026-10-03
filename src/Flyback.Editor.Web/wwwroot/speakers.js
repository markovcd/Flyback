// The editor's sound, for PageSound.cs: the web viewer's worker plays each edit, the web
// viewer's worklet is the speaker, and how far the speaker has got is the clock. Until the
// browser lets the page make a sound and the worker is up, the wall clock stands in, and
// the sound joins where the picture is. The worker works the sound out a step lower
// itself when it keeps falling behind; the editor never gives the sound up.

import { takeAudio } from '../viewer/session.js';

const worker = new Worker('../viewer/speaker.js', { type: 'module' });

let rate = 48000;
let ready = false;
let failure = null;
let error = null;

let context = null;
let volume = null;
let attached = false;
let loudness = 1;

let running = false;

/** Whether the speaker is the clock: the browser lets it play, the worker is up, and it has joined. */
let heard = false;

let generation = 0;
let origin = 0;
let played = 0;
let reportedAt = 0;

let wallStart = 0;
let held = 0;

/** The Meters last named, how many of them there are, and the latest readings of them the worker sent. */
let meters = 0;
let metered = 0;
let readings = { meters: 0, values: new Float32Array(0) };
let soundStatus = {};

/** How many values the editor's knobs and keys have handed the worker. */
let handed = 0;

worker.onmessage = ({ data }) => {
  if (data.ready) {
    ready = true;
    join();
  } else if (data.failure !== undefined) {
    failure = data.failure;
  } else if (data.readings !== undefined) {
    readings = { meters: data.meters, values: data.readings };
    if (data.status) soundStatus = data.status;
  } else if (data.edited !== undefined) {
    error = data.error ?? null;
    if (data.status) soundStatus = data.status;
  }
};

worker.onerror = event => { failure = event.message || "The sound's worker would not start."; };

// A browser holds sound back until the page is used, so any press may be the one that lets it.
// A touch's pointerdown does not count as use in WebKit; its release does.
for (const kind of ['pointerdown', 'pointerup', 'click', 'keydown']) {
  document.addEventListener(kind, () => {
    if (running && context !== null && context.state !== 'running') context.resume();
  }, { capture: true });
}

/** The speaker's count of what it has played, which is the clock while the sound is heard. */
function report({ data }) {
  if (data.generation !== generation) return;

  played = data.played;
  reportedAt = data.at;
}

function open() {
  if (context !== null) return;

  takeAudio();
  context = new AudioContext({ sampleRate: rate, latencyHint: 'interactive' });
  context.onstatechange = join;
  volume = new GainNode(context, { gain: loudness });

  context.audioWorklet.addModule('../viewer/sound.js').then(() => {
    const queue = new AudioWorkletNode(context, 'flyback-queue', { numberOfInputs: 0, outputChannelCount: [2] });
    queue.connect(volume).connect(context.destination);
    queue.port.onmessage = report;

    // The worker feeds the speaker straight, so a busy page never keeps the sound waiting.
    const channel = new MessageChannel();
    queue.port.postMessage({ feed: channel.port1 }, [channel.port1]);
    worker.postMessage({ speaker: channel.port2 }, [channel.port2]);

    attached = true;
    join();
  }).catch(reason => { failure = String(reason?.message ?? reason); });
}

/** Hands the clock to the speaker once everything it needs is there, starting it where the picture is. */
function join() {
  if (heard || !running || !ready || !attached || failure !== null || context.state !== 'running') return;

  const at = time();
  heard = true;
  seek(at);
  worker.postMessage({ run: true });
}

function seek(seconds) {
  generation++;
  origin = held = seconds;
  played = 0;
  wallStart = performance.now();
  worker.postMessage({ seek: seconds, generation });
}

export function time() {
  if (!running) return held;

  if (heard) {
    const since = played > 0 ? Math.min(Math.max(context.currentTime - reportedAt, 0), 0.1) : 0;
    held = origin + played / rate + since;
  } else {
    held = origin + (performance.now() - wallStart) / 1000;
  }

  return held;
}

export function start(sampleRate) {
  if (running) return;

  rate = sampleRate;
  running = true;

  // A queue left by a stop carries on where it stopped; on the wall clock, the picture does.
  if (!heard) {
    origin = held;
    wallStart = performance.now();
  }

  open();
  context.resume().catch(() => {});
  worker.postMessage({ run: heard });
  join();
}

export function stop() {
  if (!running) return;

  time();
  running = false;
  worker.postMessage({ run: false });
  if (heard) context.suspend();
}

export function seekTo(seconds) {
  seek(seconds);
}

export function gain(level) {
  loudness = level;
  if (volume !== null) volume.gain.value = level;
}

/** Works the sound out at <factor> times the output rate from here on. */
export function oversample(factor) {
  worker.postMessage({ oversample: factor });
  soundStatus.oversample = factor;
}

/** The factor the worker last said it plays at, lowered by it or not, or 0 before it has said. */
export function oversampleNow() {
  return soundStatus.oversample ?? 0;
}

/** How many times real time the worker last said the sound renders at, or 0 before it has said. */
export function speed() {
  return soundStatus.speed ?? 0;
}

export function aspect(value) {
  worker.postMessage({ aspect: value });
}

export function edit(text, aspect) {
  worker.postMessage({ edit: text, aspect });
}

export function keep(path, bytes) {
  const copy = new Uint8Array(bytes);
  worker.postMessage({ keep: path, bytes: copy }, [copy.buffer]);
}

export function forget() {
  worker.postMessage({ forget: true });
}

export function play(keys, values) {
  handed += keys.length;
  worker.postMessage({ play: keys, values: Array.from(values) });
}

/**
 * Names the Meters the picture reads and the charts it draws, by module, window and
 * whether each is a spectrum, and answers the number their readings will come back under.
 */
export function watch(keys, charts, windows, spectra) {
  metered = keys.length;
  worker.postMessage({ watch: keys, charts, windows: Array.from(windows), spectra: Array.from(spectra), meters: ++meters });
  return meters;
}

/** The latest readings named under <number>, the Meters' and then each chart's buffer, or none where they are of others. */
export function read(number) {
  return readings.meters === number ? Array.from(readings.values) : [];
}

/** What a script driving the page can read of the sound. */
export function status() {
  return {
    running, heard, ready, failure, error,
    context: context?.state ?? null,
    time: time(), played, generation,
    soundOps: soundStatus.soundOps ?? 0,
    speed: soundStatus.speed ?? 0,
    queued: soundStatus.queued ?? 0,
    backend: soundStatus.soundBackend ?? null,
    interpreted: soundStatus.interpreted ?? null,
    oversample: soundStatus.oversample ?? null,
    timed: soundStatus.timed ?? 0,
    late: soundStatus.late ?? 0,
    volume: loudness,
    handed,
    meters: Array.from(readings.values.subarray(0, metered)),
  };
}
