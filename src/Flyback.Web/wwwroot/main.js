// The page around WebExports: opens a patch, keeps the speaker's queue topped up,
// draws each frame at the time the speaker has reached, and answers window.flyback
// for anything that wants to drive it without looking.

import { dotnet } from './_framework/dotnet.js';
import * as gl from './gl.js';

const params = new URLSearchParams(location.search);
const [width, height] = (params.get('size') ?? '960x540').split('x').map(Number);
const looped = params.has('loop');

/** Frames rendered per call, and how far ahead of the speaker the queue is kept. */
const CHUNK = 1024;
const AHEAD = 0.25;

/** Below this many seconds rendered per second spent, the sound would stutter, so the picture plays alone. */
const FAST_ENOUGH = 1.2;

const $ = id => document.getElementById(id);
const ui = {
  presets: $('presets'), file: $('file'), play: $('play'), rewind: $('rewind'), mute: $('mute'),
  clock: $('clock'), canvas: $('screen'), cover: $('cover'), status: $('status'),
};

const runtime = await dotnet.create();
runtime.setModuleImports('gl', gl);
const flyback = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Flyback.Web.WebExports;

let info = null;
const noPicture = gl.attach(ui.canvas, () => runtime.localHeapViewU8());
let error = null;
let warning = null;
let name = '';

let playing = false;
let pausedAt = 0;
let origin = 0;
let wallStart = 0;
let drawnAt = NaN;
let drawnSize = '';

let soundAllowed = true;
let muted = params.has('mute');
let context = null;
let queue = null;
let volume = null;
let generation = 0;
let sent = 0;
let played = 0;
let starved = 0;
let reportedAt = 0;

/** Whether the speaker is the clock: only while the queue it plays starts where the picture is. */
let heard = false;

/** Where the patch is: the speaker's position while it plays, the wall clock where there is no sound. */
function now() {
  if (!playing) return pausedAt;

  if (heard) {
    const since = played > 0 ? Math.min(Math.max(context.currentTime - reportedAt, 0), 0.1) : 0;
    return origin + played / info.sampleRate + since;
  }

  return origin + (performance.now() - wallStart) / 1000;
}

function pump() {
  if (!playing || !heard) return;

  const rate = info.sampleRate;

  // A few chunks a call at most, so a patch too heavy to keep up stutters rather than freezing the page.
  for (let i = 0; i < 8 && (sent - played) / rate < AHEAD; i++) {
    const at = flyback.Hear(CHUNK) / 4;
    const chunk = runtime.localHeapViewF32().slice(at, at + CHUNK * 2);
    queue.port.postMessage(chunk, [chunk.buffer]);
    sent += CHUNK;
  }
}

function report({ data }) {
  if (data.generation !== generation) return;

  played = data.played;
  starved = data.starved;
  reportedAt = data.at;
  pump();
}

/** Starts the speaker, and says whether the browser let it: it holds sound back until the page is clicked. */
async function startSound() {
  if (context === null) {
    context = new AudioContext({ sampleRate: info.sampleRate, latencyHint: 'playback' });
    await context.audioWorklet.addModule('sound.js');

    queue = new AudioWorkletNode(context, 'flyback-queue', { numberOfInputs: 0, outputChannelCount: [2] });
    volume = new GainNode(context, { gain: muted ? 0 : 1 });
    queue.connect(volume).connect(context.destination);
    queue.port.onmessage = report;
  }

  await Promise.race([context.resume(), new Promise(resolve => setTimeout(resolve, 250))]);

  return context.state === 'running';
}

/** Empties the speaker's queue and starts counting from <seconds>. */
function restartQueue() {
  generation++;
  sent = played = 0;
  queue?.port.postMessage({ clear: generation });
}

function seek(seconds) {
  flyback.Seek(seconds);
  origin = pausedAt = seconds;
  wallStart = performance.now();
  drawnAt = NaN;
  restartQueue();
  pump();
}

/** Why the sound is not playing along, when it is not and could. */
let held = null;

async function play() {
  if (info === null || playing) return;

  if (pausedAt >= info.length) seek(0);

  // A queue left by a pause carries on where it stopped; anything else starts again where the picture is.
  const carryOn = heard;
  heard = soundAllowed && (await startSound());

  if (heard && !carryOn) seek(pausedAt);

  held = soundAllowed && !heard ? 'The browser holds the sound back until the page is clicked, so the picture plays alone.' : null;

  if (!heard) {
    origin = pausedAt;
    wallStart = performance.now();
  }

  playing = true;
  ui.cover.hidden = true;
  pump();
  paint();
}

function pause() {
  if (!playing) return;

  pausedAt = now();
  playing = false;

  // The queue stays where it is, so play carries on from the very next sample.
  if (heard) context.suspend();
  paint();
}

/** Stops, and forgets the speaker's queue, so the next play starts both halves at one moment. */
function stop() {
  pause();
  heard = false;
}

function toggleMute() {
  if (!soundAllowed) {
    // Asked to hear a patch too heavy to keep up: its sound joins where the picture is.
    const was = playing;

    stop();
    soundAllowed = true;
    warning = `${warning ?? ''} Sound on anyway; expect it to stutter.`.trim();
    if (was) play();
  } else {
    muted = !muted;
    if (volume) volume.gain.value = muted ? 0 : 1;
  }

  paint();
}

async function open(opening, label) {
  const was = playing;

  stop();
  error = opening();
  warning = null;
  name = label;
  info = null;

  if (error === null) {
    info = JSON.parse(flyback.Status());

    const speed = flyback.Measure(0.2);
    soundAllowed = speed >= FAST_ENOUGH;

    if (!soundAllowed)
      warning = `This patch's sound renders at ${speed.toFixed(2)}× real time here, so the picture plays alone. Press 🔇 to hear it anyway.`;

    document.title = `${label} · Flyback Viewer`;
  }

  seek(0);
  ui.cover.hidden = error !== null ? false : was;
  ui.cover.textContent = error ?? '▶  Click to play';

  if (was && error === null) await play();
  paint();
}

async function openPreset(preset) {
  ui.presets.value = preset;
  await open(() => flyback.OpenPreset(preset, width, height), preset);
}

async function openBytes(file, bytes) {
  return open(() => flyback.OpenFile(file, bytes, width, height), file);
}

async function openUrl(url) {
  const response = await fetch(url);
  if (!response.ok) {
    error = `${url}: ${response.status} ${response.statusText}`;
    paint();
    return;
  }

  await openBytes(url.split('/').pop().split('?')[0], new Uint8Array(await response.arrayBuffer()));
}

const clockText = seconds => {
  const whole = Math.max(0, Math.floor(seconds));
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`;
};

/** The header and the status line, which say where things are and what is wrong. */
function paint() {
  const ready = info !== null;

  ui.play.disabled = ui.rewind.disabled = ui.mute.disabled = !ready;
  ui.play.textContent = playing ? '⏸' : '▶';
  ui.play.setAttribute('aria-label', playing ? 'Pause' : 'Play');
  ui.mute.textContent = !soundAllowed || muted ? '🔇' : '🔊';
  ui.clock.textContent = ready ? `${clockText(now())} / ${clockText(info.length)}` : '';

  const parts = [];

  if (ready) {
    const status = JSON.parse(flyback.Status());
    parts.push(`${status.soundOps} sound ops · ${status.pictureOps} picture ops · ${status.width}×${status.height}`);
    if (status.speed > 0) parts.push(`sound renders at ${status.speed.toFixed(2)}×`);
    if (starved > 0) parts.push(`${starved} dropouts`);
    if (status.linking) parts.push('building the shader…');
  }

  ui.status.replaceChildren(parts.join(' · '));

  for (const [text, kind] of [[warning, 'warn'], [held, 'warn'], [noPicture, 'error'], [error, 'error']]) {
    if (!text) continue;

    const span = document.createElement('span');
    span.className = kind;
    span.textContent = ` ${text}`;
    ui.status.append(span);
  }
}

function frame() {
  requestAnimationFrame(frame);
  if (info === null || noPicture !== null) return;

  const dpr = window.devicePixelRatio || 1;
  const w = Math.max(1, Math.round(ui.canvas.clientWidth * dpr));
  const h = Math.max(1, Math.round(ui.canvas.clientHeight * dpr));

  if (ui.canvas.width !== w || ui.canvas.height !== h) {
    ui.canvas.width = w;
    ui.canvas.height = h;
  }

  let t = now();

  if (playing && t >= info.length) {
    if (looped) {
      seek(0);
      t = 0;
    } else {
      pause();
      pausedAt = t = info.length;
    }
  }

  const size = `${w}x${h}`;

  // Drawing again at the same moment would run a feedback loop on while paused.
  if (t === drawnAt && size === drawnSize && !flyback.Linking()) return;

  const failure = flyback.Draw(t, w, h);
  if (failure && failure !== error) {
    error = failure;
    paint();
  }

  drawnAt = t;
  drawnSize = size;
}

ui.play.onclick = () => (playing ? pause() : play());
ui.cover.onclick = () => { if (info !== null) play(); };
ui.rewind.onclick = () => { seek(0); paint(); };
ui.mute.onclick = toggleMute;
ui.presets.onchange = () => openPreset(ui.presets.value);

ui.file.onchange = async () => {
  const file = ui.file.files[0];
  if (file) await openBytes(file.name, new Uint8Array(await file.arrayBuffer()));
};

document.addEventListener('dragover', event => event.preventDefault());
document.addEventListener('drop', async event => {
  event.preventDefault();
  const file = event.dataTransfer.files[0];
  if (file) await openBytes(file.name, new Uint8Array(await file.arrayBuffer()));
});

document.addEventListener('keydown', event => {
  if (event.target instanceof HTMLSelectElement) return;
  if (event.code === 'Space') { event.preventDefault(); playing ? pause() : play(); }
  if (event.code === 'Home') { seek(0); paint(); }
});

window.flyback = {
  presets: () => flyback.Presets().split('\n'),
  open: openPreset,
  openUrl,
  play,
  pause,
  seek: seconds => { seek(seconds); paint(); },
  status: () => ({
    ...JSON.parse(flyback.Status()),
    name, playing, time: now(), sound: heard, soundAllowed, held, muted,
    queued: info ? (sent - played) / info.sampleRate : 0, starved, warning, error,
  }),
  snapshot: () => ui.canvas.toDataURL('image/png'),
  still,
};

/** The frame at <seconds> as a PNG data URL at the patch's own size, or null where there is none. */
function still(seconds) {
  if (info === null) return null;

  const at = flyback.Still(seconds);
  drawnAt = NaN;
  if (at === 0) return null;

  const { width: w, height: h } = info;
  const bytes = runtime.localHeapViewU8();
  const image = new ImageData(w, h);

  // Read back bottom row first, as GL keeps it.
  for (let row = 0; row < h; row++)
    image.data.set(bytes.subarray(at + (h - 1 - row) * w * 4, at + (h - row) * w * 4), row * w * 4);

  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  canvas.getContext('2d').putImageData(image, 0, 0);

  return canvas.toDataURL('image/png');
}

const presets = window.flyback.presets();
for (const preset of presets) ui.presets.add(new Option(preset, preset));
ui.presets.disabled = false;

setInterval(pump, 10);
setInterval(paint, 250);
requestAnimationFrame(frame);

if (params.has('file')) {
  await openUrl(params.get('file'));
} else {
  const wanted = params.get('preset') ?? 'Beat you can see';
  ui.presets.value = presets.find(p => p.toLowerCase() === wanted.toLowerCase()) ?? presets[0];
  await openPreset(ui.presets.value);
}

if (noPicture !== null) ui.cover.textContent = `${noPicture} The sound still plays: click to start it.`;
