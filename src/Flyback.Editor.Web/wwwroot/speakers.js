// The editor's sound, for PageSound.cs, on the web viewer's speakers: its worker plays each
// edit. Until the browser lets the page make a sound and the worker is up, the wall clock
// stands in, and the sound joins where the picture is. The worker works the sound out a step lower
// itself when it keeps falling behind; the editor never gives the sound up.

import { Speakers } from '../viewer/speakers.js';

const speakers = new Speakers();
const worker = speakers.worker;
speakers.onState = join;

let rate = 48000;
let ready = false;
let failure = null;
let error = null;
let loudness = 1;

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
    if (speakers.running && speakers.state !== null && speakers.state !== 'running') resume();
  }, { capture: true });
}

/** Asks the browser to let the speakers play, and joins them once it does. */
function resume() {
  speakers.resume(rate).then(join, reason => { failure = String(reason?.message ?? reason); });
}

/** Hands the clock to the speakers once everything they need is there, starting them where the picture is. */
function join() {
  if (speakers.heard || !speakers.running || !ready || !speakers.attached || failure !== null || speakers.state !== 'running') return;

  speakers.seek(speakers.time());
  speakers.start(true);
}

export function time() {
  return speakers.time();
}

export function start(sampleRate) {
  if (speakers.running) return;

  rate = sampleRate;

  // A queue left by a stop carries on where it stopped; on the wall clock, the picture does.
  speakers.start(speakers.heard);
  resume();
  join();
}

export function stop() {
  if (speakers.running) speakers.stop();
}

/** Opens the microphone for a Line In, or lets it go. */
export function listen(on) {
  speakers.microphone.want(on);
}

/** Has <say> told of a sentence whenever the microphone will not open or stops. */
export function onMicrophoneTrouble(say) {
  speakers.microphone.onTrouble = say;
}

export function seekTo(seconds) {
  speakers.seek(seconds);
}

export function gain(level) {
  loudness = level;
  speakers.gain(level);
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
 * kind (trace, spectrum or beam), and answers the number their readings will come back under.
 */
export function watch(keys, charts, windows, kinds) {
  metered = keys.length;
  worker.postMessage({ watch: keys, charts, windows: Array.from(windows), kinds: Array.from(kinds), meters: ++meters });
  return meters;
}

/** The latest readings named under <number>, the Meters' and then each chart's buffer, or none where they are of others. */
export function read(number) {
  return readings.meters === number ? Array.from(readings.values) : [];
}

/** What a script driving the page can read of the sound. */
export function status() {
  return {
    ...speakers.status(),
    ready, failure, error,
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
    lineIn: soundStatus.lineIn ?? false,
    meters: Array.from(readings.values.subarray(0, metered)),
  };
}
