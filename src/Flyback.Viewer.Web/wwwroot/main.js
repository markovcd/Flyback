// The page around WebExports: opens a patch's picture here and its sound in speaker.js,
// a worker of its own, draws each frame at the time the speaker has reached, and answers
// window.flyback for anything that wants to drive it without looking.

import { dotnet } from './_framework/dotnet.js';
import * as gl from './gl.js';
import { takeAudio, throughElement } from './session.js';
import { onTap } from './tap.js';

const params = new URLSearchParams(location.search);
/** Whether the patch's length plays round and round; the Loop button flips it. */
let looped = params.has('loop');

/** Playing a file rather than a shipped preset. */
const preview = params.has('file');

/** The sizes offered: the editor's own, up to 1080p. */
const SIZES = [[320, 180], [480, 270], [640, 360], [960, 540], [1280, 720], [1920, 1080], [1024, 768]];

/** The size list's choice that draws no picture, leaving the sound to play alone. */
const OFF = 'off';

/** Whether the picture is drawn; the sound plays either way. */
let pictureOn = params.get('size') !== OFF;

let [width, height] = (pictureOn ? params.get('size') ?? '960x540' : '960x540').split('x').map(Number);
if (!(width > 0 && height > 0)) [width, height] = [960, 540];

/** Below this many seconds rendered per second spent, the sound would stutter, so the picture plays alone. */
const FAST_ENOUGH = 1.2;


/** Where the browser keeps the volume between visits. */
const VOLUME_KEPT = 'flyback-viewer-volume';

const $ = id => document.getElementById(id);
const ui = {
  file: $('file'), size: $('size'), back: $('back'), edit: $('edit'),
  play: $('play'), rewind: $('rewind'), loop: $('loop'), seek: $('seek'), mute: $('mute'), volume: $('volume'), fullscreen: $('fullscreen'),
  panel: $('panel'), grip: $('grip'), about: $('about'), credit: $('credit'), byline: $('byline'), initial: $('initial'), author: $('author'), tags: $('tags'), keyboard: $('keyboard'), notes: $('notes'), notesHome: $('notes-home'),
  sheet: $('sheet'), sizer: $('sizer'), find: $('find'), resetAll: $('reset-all'), none: $('none'),
  keybed: $('keybed'), keysSaid: $('keys-said'), octave: $('octave'), octaveDown: $('octave-down'), octaveUp: $('octave-up'),
  title: $('title'), clock: $('clock'), length: $('length'),
  main: document.querySelector('main'), canvas: $('screen'), off: $('off'), cover: $('cover'), status: $('status'),
};

/** The panel's tabs, which only a phone shows, each with the pane it opens. */
const tabs = ['controls', 'keys', 'about'].map(name => ({ name, tab: $(`tab-${name}`), pane: $(name === 'about' ? 'about-pane' : name) }));

/** A screen too narrow for the panel beside the picture, which puts it in a sheet under it. */
const narrow = matchMedia('(max-width: 640px)');

/** The sound's thread, what it last said of the sound, and why it stopped where it did. */
const speaker = new Worker('speaker.js', { type: 'module' });
let soundStatus = {};
let speakerFailure = null;

/** Which open is the latest, and the opens still waiting on the worker's answer. */
let opens = 0;
const replies = new Map();

const speakerReady = new Promise(resolve => {
  const failed = text => {
    speakerFailure = text;
    resolve(false);

    for (const reply of replies.values()) reply({ error: null, status: null, speed: null });
  };

  speaker.onmessage = ({ data }) => {
    if (data.ready) resolve(true);
    else if (data.failure !== undefined) failed(data.failure);
    else if (data.state !== undefined) hearState(data);
    else if (data.opened !== undefined) replies.get(data.opened)?.(data);
  };

  speaker.onerror = event => failed(event.message || "The sound's worker would not start.");
});

const runtime = await dotnet.create();
runtime.setModuleImports('gl', gl);
const flyback = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Flyback.Viewer.Web.WebExports;

let info = null;
const noPicture = gl.attach(ui.canvas, () => runtime.localHeapViewU8());
let error = null;
let warning = null;
let name = '';

/** Opens the patch on screen again at a size, which is how a new resolution takes. */
let opener = null;

/** What the open patch came from, for the editor: a preset's name, an address, or a file's bytes. */
let source = null;

/** Whether the seek bar is held, which is when the playhead leaves it alone. */
let dragging = false;

let playing = false;
let pausedAt = 0;
let origin = 0;
let wallStart = 0;
let drawnAt = NaN;
let drawnSize = '';

let soundAllowed = true;
let muted = params.has('mute');

/** How loud, 0 to 1, as it was left on the last visit where the browser keeps it. */
let loudness = (() => {
  try {
    const kept = localStorage.getItem(VOLUME_KEPT);
    return kept === null ? 1 : Math.min(Math.max(Number(kept) || 0, 0), 1);
  } catch {
    return 1;
  }
})();
let context = null;
let queue = null;
let volume = null;

/** The <audio> element the sound plays through where the page must take audio focus, else null. */
let element = null;
let generation = 0;
let played = 0;
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

/** The speaker's count of what it has played, which is the clock while the sound is heard. */
function report({ data }) {
  if (data.generation !== generation) return;

  played = data.played;
  reportedAt = data.at;
}

/** Hands the picture the Meters' readings the worker packed, when they are of the patch open now. */
function hearState({ opened, state, status }) {
  if (opened !== opens) return;

  if (status) {
    const was = soundStatus;
    soundStatus = status;
    follow(was);
  }

  if (info === null || state.length === 0 || state.length !== info.stateLength) return;

  const at = flyback.Heard() / 4;
  if (at === 0) return;

  runtime.localHeapViewF32().set(state, at);
  flyback.Apply();
}

/** The worker's answer to opening the sound, or none where it has failed. */
async function openSound(what, id) {
  if (!(await speakerReady)) return { error: null, status: null, speed: null };

  return new Promise(resolve => {
    replies.set(id, reply => {
      replies.delete(id);
      resolve(reply);
    });

    speaker.postMessage({ open: what, width, height, opened: id });
  });
}

/** Both halves' status: the worker's last word on the sound, and the picture's now. */
function status() {
  return { ...soundStatus, ...JSON.parse(flyback.Status()) };
}

/** Starts the speaker, and says whether the browser let it: it holds sound back until the page is clicked. */
async function startSound() {
  if (context === null) {
    takeAudio();
    context = new AudioContext({ sampleRate: info.sampleRate, latencyHint: 'interactive' });
    await context.audioWorklet.addModule('sound.js');

    queue = new AudioWorkletNode(context, 'flyback-queue', { numberOfInputs: 0, outputChannelCount: [2] });
    volume = new GainNode(context, { gain: muted ? 0 : loudness });
    queue.connect(volume);
    element = throughElement(context, volume);
    if (element === null) volume.connect(context.destination);
    queue.port.onmessage = report;

    // The worker feeds the speaker straight, so a busy page never keeps the sound waiting.
    const channel = new MessageChannel();
    queue.port.postMessage({ feed: channel.port1 }, [channel.port1]);
    speaker.postMessage({ speaker: channel.port2 }, [channel.port2]);
  }

  await Promise.race([context.resume(), new Promise(resolve => setTimeout(resolve, 250))]);

  const running = context.state === 'running';
  if (running) element?.play().catch(() => {});

  return running;
}

/** Both halves to <seconds>, the speaker's queue emptied and counted again from there. */
function seek(seconds) {
  flyback.Seek(seconds);
  origin = pausedAt = seconds;
  wallStart = performance.now();
  drawnAt = NaN;

  generation++;
  played = 0;
  speaker.postMessage({ seek: seconds, generation });
}

/** Where the open patch ends, in seconds: never, for one that has not said how long it plays. */
const end = () => info?.length ?? Infinity;

/** Why the sound is not playing along, when it is not and could. */
let held = null;

async function play() {
  if (info === null || playing) return;

  if (pausedAt >= end()) seek(0);

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
  speaker.postMessage({ run: heard });
  keepAwake();
  paint();
}

function pause() {
  if (!playing) return;

  pausedAt = now();
  playing = false;

  // The queue stays where it is, so play carries on from the very next sample.
  speaker.postMessage({ run: false });
  if (heard) context.suspend();
  element?.pause();
  keepAwake();
  paint();
}

/** Stops, and forgets the speaker's queue, so the next play starts both halves at one moment. */
function stop() {
  pause();
  heard = false;
}

/** Whether the sound was asked for after it was found too slow, which is never taken back. */
let insisted = false;

function tooSlow(speed) {
  soundAllowed = false;
  console.warn(`This patch's sound renders at ${speed.toFixed(2)}× real time here, so the picture plays alone. Press the speaker to hear it anyway.`);
}

/** An oversampling factor as the console says it; 1× is none. */
function oversampling(factor) {
  return factor > 1 ? `${factor}× oversampling` : 'no oversampling';
}

/**
 * Says so when the worker, which judges its own sound once it plays, has worked it out a
 * step lower since <was>, and hands the picture the clock when it falls behind with none too.
 */
function follow(was) {
  if (!playing || !heard || insisted) return;

  if (soundStatus.behind) {
    stop();
    tooSlow(soundStatus.speed ?? 0);
    play();
  } else if (soundStatus.oversample < was.oversample) {
    console.warn(`The sound kept falling behind at ${oversampling(was.oversample)}, so it is worked out with ${oversampling(soundStatus.oversample)} now.`);
  }
}

function toggleMute() {
  if (!soundAllowed) {
    // Asked to hear a patch too heavy to keep up: its sound joins where the picture is.
    const was = playing;

    stop();
    soundAllowed = true;
    insisted = true;
    console.warn('Sound on anyway; expect it to stutter.');
    if (was) play();
  } else {
    muted = !muted;
    if (volume) volume.gain.value = muted ? 0 : loudness;
  }

  paint();
}

/** Sets whether the patch's length plays round and round. */
function setLooped(on) {
  looped = Boolean(on);
  paint();
}

/** Sets how loud, 0 to 1; turning it up takes the mute off. */
function setVolume(level) {
  loudness = Math.round(Math.min(Math.max(Number(level) || 0, 0), 1) * 100) / 100;
  if (loudness > 0) muted = false;
  if (volume) volume.gain.value = muted ? 0 : loudness;

  try {
    localStorage.setItem(VOLUME_KEPT, String(loudness));
  } catch {
    // Kept for this visit only.
  }

  paint();
}

/** The computer keyboard's keys held down, by the browser's name for each, with the note it struck. */
const pressed = new Map();

/** Whether the open patch is played on the computer keyboard, which is when its keys are notes. */
const playable = () => info?.played === true && speakerFailure === null;

/** A note on the computer keyboard, <down> or up, for the sound's worker, which hands the picture its voice. */
function strike(note, down = true) {
  speaker.postMessage({ strike: note, down });
}

/** Every computer keyboard note let go: when the page loses the keys, or before they move. */
function release() {
  if (pressed.size === 0) return;

  pressed.clear();
  speaker.postMessage({ release: true });
}

/** Where the computer keyboard's notes are, as it was last said. */
let keyboardSaid = null;

/** A key the computer keyboard plays taken as a note, and true; false for any other key. */
function typed(event, down) {
  if (!playable() || event.ctrlKey || event.metaKey || event.altKey) return false;
  if (event.target instanceof HTMLSelectElement || typing(event.target)) return false;

  if (down && (event.code === 'PageUp' || event.code === 'PageDown')) {
    event.preventDefault();
    release();
    keyboardSaid = flyback.Shift(event.code === 'PageUp' ? 1 : -1);
    paint();
    return true;
  }

  if (!down) {
    if (!pressed.has(event.code)) return false;

    strike(pressed.get(event.code), false);
    pressed.delete(event.code);
    return true;
  }

  const note = flyback.KeyNote(event.code);
  if (note < 0) return false;

  event.preventDefault();

  if (!event.repeat && !pressed.has(event.code)) {
    pressed.set(event.code, note);
    strike(note);
  }

  return true;
}

/** Whether <target> takes typing, which is when a letter is a letter and not a note or a shortcut. */
function typing(target) {
  return target instanceof HTMLTextAreaElement || (target instanceof HTMLInputElement && target.type !== 'range');
}

/** The panel's knobs: each one's key, name, where it rests and where it is turned to. */
let knobs = [];

/**
 * Lays out a knob for each of the open patch's knobs. Knobs of the same patch opened
 * again keep where they were turned to, and the worker is told.
 */
function buildPanel(open, keep) {
  const before = new Map(knobs.map(knob => [knob.key, knob.value]));

  knobs = open ? JSON.parse(flyback.Knobs()).map(knob => ({ ...knob, value: knob.rest, turns: 0, shown: 0 })) : [];
  ui.panel.replaceChildren(...knobs.map(dial));
  for (const count of document.querySelectorAll('.count')) count.textContent = knobs.length;
  find();

  for (const knob of knobs)
    if (keep && before.has(knob.key) && before.get(knob.key) !== knob.value) turn(knob, before.get(knob.key), true);
}

/** Shows the knobs whose names hold what the search box does. */
function find() {
  const wanted = ui.find.value.trim().toLowerCase();
  let shown = 0;

  for (const knob of knobs) {
    knob.cell.hidden = wanted !== '' && !knob.name.toLowerCase().includes(wanted);
    if (!knob.cell.hidden) shown++;
  }

  ui.none.hidden = shown > 0 || knobs.length === 0;
}

/** Where the browser keeps the sheet's height between visits. */
const PANEL_KEPT = 'flyback-viewer-panel';

/** The sheet's heights a tap on its grip steps through: closed to its tabs, half the screen, most of it. */
const SHEET_STEPS = [88, 0.45, 0.7];

/** Sets how tall the phone's sheet stands, in pixels, or back to its default for null. */
function sizePanel(pixels, keep = true) {
  if (pixels === null) ui.sheet.style.removeProperty('--sheet-height');
  else ui.sheet.style.setProperty('--sheet-height', `${Math.round(Math.min(Math.max(pixels, 0), innerHeight * 0.8))}px`);

  if (!keep) return;

  try {
    if (pixels === null) localStorage.removeItem(PANEL_KEPT);
    else localStorage.setItem(PANEL_KEPT, String(pixels));
  } catch { /* Storage is off; the height just isn't kept. */ }
}

/** The sheet's next height up, or closed again from the tallest. */
function stepPanel() {
  const height = ui.sheet.getBoundingClientRect().height;
  const tallest = parseFloat(getComputedStyle(ui.sheet).maxHeight) || Infinity;
  const steps = SHEET_STEPS.map(step => Math.min(step < 1 ? step * innerHeight : step, tallest));
  sizePanel(steps.find(step => step > height + 8) ?? steps[0]);
}

function panelResizing() {
  try {
    const kept = Number(localStorage.getItem(PANEL_KEPT));
    if (kept > 0) sizePanel(kept, false);
  } catch { /* Nothing kept. */ }

  let from = null;
  const height = () => ui.sheet.getBoundingClientRect().height;

  ui.grip.onpointerdown = event => {
    from = { y: event.clientY, height: height(), moved: false };
    ui.grip.setPointerCapture(event.pointerId);
  };
  ui.grip.onpointermove = event => {
    if (from === null || (!from.moved && Math.abs(event.clientY - from.y) < 6)) return;

    from.moved = true;
    sizePanel(from.height + from.y - event.clientY);
  };
  ui.grip.onpointerup = () => {
    if (from !== null && !from.moved) stepPanel();
    from = null;
  };
  ui.grip.onpointercancel = () => { from = null; };
  ui.grip.ondblclick = () => sizePanel(null);
  ui.grip.onkeydown = event => {
    const step = { ArrowUp: 24, ArrowDown: -24 }[event.key];
    if (step !== undefined) sizePanel(height() + step);
    else if (event.key === 'Home') sizePanel(null);
    else if (event.key === 'Enter' || event.key === ' ') stepPanel();
    else return;
    event.preventDefault();
  };
}

panelResizing();

/** A point on the knob's ring, <angle> degrees clockwise from three o'clock, <radius> from its middle. */
const onRing = (angle, radius) => [32 + radius * Math.cos(angle * Math.PI / 180), 32 + radius * Math.sin(angle * Math.PI / 180)];

/** Where a knob's travel starts, in degrees from three o'clock, and how far it goes. */
const FROM = 135, SWEEP = 270;

const SVG = 'http://www.w3.org/2000/svg';

function dial(knob) {
  const cell = document.createElement('div');
  const face = document.createElement('div');
  const svg = document.createElementNS(SVG, 'svg');
  const track = document.createElementNS(SVG, 'path');
  const arc = document.createElementNS(SVG, 'path');
  const cap = document.createElementNS(SVG, 'circle');
  const pointer = document.createElementNS(SVG, 'line');
  const input = document.createElement('input');
  const called = document.createElement('span');
  const reading = document.createElement('output');

  const [x0, y0] = onRing(FROM, 26), [x1, y1] = onRing(FROM + SWEEP, 26);
  track.setAttribute('d', `M${x0} ${y0} A26 26 0 1 1 ${x1} ${y1}`);
  track.classList.add('track');
  arc.classList.add('arc');
  cap.setAttribute('cx', '32');
  cap.setAttribute('cy', '32');
  cap.setAttribute('r', '19');
  svg.setAttribute('viewBox', '0 0 64 64');
  svg.setAttribute('aria-hidden', 'true');
  svg.append(track, arc, cap, pointer);

  input.type = 'range';
  input.min = '0';
  input.max = '1';
  input.step = '0.01';
  input.value = knob.value;
  input.setAttribute('aria-label', knob.name);
  input.oninput = () => turn(knob, Number(input.value));

  face.className = 'knob';
  face.title = `${knob.name}: drag to turn, double-click to put it back`;
  face.append(svg, input);

  // Up or right turns it up, two hundred pixels end to end, a tenth as fast with Shift held.
  let from = null;
  face.onpointerdown = event => {
    if (event.button !== 0) return;
    event.preventDefault();
    input.focus({ preventScroll: true });
    face.setPointerCapture(event.pointerId);
    from = { x: event.clientX, y: event.clientY, value: knob.value };
  };
  face.onpointermove = event => {
    if (from === null) return;
    const moved = (from.y - event.clientY) + (event.clientX - from.x);
    turn(knob, from.value + moved / 200 * (event.shiftKey ? 0.1 : 1));
  };
  face.onpointerup = face.onpointercancel = () => { from = null; };
  face.ondblclick = () => turn(knob, knob.rest);

  called.className = 'name';
  called.textContent = called.title = knob.name;
  cell.className = 'cell';
  cell.append(face, called, reading);

  knob.cell = cell;
  knob.input = input;
  knob.reading = reading;
  knob.draw = () => {
    const angle = FROM + SWEEP * knob.value;
    const [ax, ay] = onRing(angle, 26), [px0, py0] = onRing(angle, 7), [px1, py1] = onRing(angle, 16);
    arc.setAttribute('d', `M${x0} ${y0} A26 26 0 ${SWEEP * knob.value > 180 ? 1 : 0} 1 ${ax} ${ay}`);
    pointer.setAttribute('x1', px0);
    pointer.setAttribute('y1', py0);
    pointer.setAttribute('x2', px1);
    pointer.setAttribute('y2', py1);
  };

  reading.textContent = Math.round(knob.value * 100);
  knob.draw();

  return cell;
}

/** The note names a key is labeled with, from C. */
const NOTE_NAMES = ['C', 'C♯', 'D', 'D♯', 'E', 'F', 'F♯', 'G', 'G♯', 'A', 'A♯', 'B'];

const noteName = note => `${NOTE_NAMES[note % 12]}${Math.floor(note / 12) - 1}`;

/**
 * The keys the screen offers, as the computer keyboard plays them: its home row in a
 * patch's scale, or a piano's octave from Z to the comma.
 */
const SCALE_ROW = ['KeyA', 'KeyS', 'KeyD', 'KeyF', 'KeyG', 'KeyH', 'KeyJ', 'KeyK', 'KeyL', 'Semicolon', 'Quote', 'Backslash'];
const PIANO_ROW = ['KeyZ', 'KeyS', 'KeyX', 'KeyD', 'KeyC', 'KeyV', 'KeyG', 'KeyB', 'KeyH', 'KeyN', 'KeyJ', 'KeyM', 'Comma'];
const KEY_LABELS = { Semicolon: ';', Quote: "'", Backslash: '\\', Comma: ',' };

/** Lays out a button for each note the screen offers, which plays as its computer keyboard key does. */
function buildKeys() {
  release();

  const scaled = flyback.KeyNote('KeyA') >= 0;
  const row = (scaled ? SCALE_ROW : PIANO_ROW)
    .map(code => ({ code, note: flyback.KeyNote(code) }))
    .filter(key => key.note >= 0);

  ui.keybed.replaceChildren(...row.map(({ code, note }) => {
    const button = document.createElement('button');
    const name = document.createElement('span');
    const letter = document.createElement('span');
    const id = `screen:${code}`;

    name.className = 'note';
    name.textContent = noteName(note);
    letter.className = 'key';
    letter.textContent = KEY_LABELS[code] ?? code.replace('Key', '');
    button.append(name, letter);
    button.setAttribute('aria-label', noteName(note));
    if (!scaled && [1, 3, 6, 8, 10].includes(note % 12)) button.classList.add('sharp');

    const up = () => {
      if (!pressed.has(id)) return;
      strike(note, false);
      pressed.delete(id);
      button.classList.remove('down');
    };

    button.onpointerdown = event => {
      event.preventDefault();
      button.releasePointerCapture?.(event.pointerId);
      if (pressed.has(id)) return;
      pressed.set(id, note);
      strike(note);
      button.classList.add('down');
    };
    button.onpointerup = button.onpointerleave = button.onpointercancel = up;
    button.oncontextmenu = event => event.preventDefault();

    return button;
  }));

  ui.octave.textContent = row.length > 0 ? noteName(row[0].note) : '';
  // The scale's name only: "Keyboard: A Aeolian (minor) on A to J, …" says "A Aeolian (minor)".
  ui.keysSaid.textContent = (keyboardSaid ?? info?.keyboard ?? '').replace(/^Keyboard: /, '').split(/ on |, /)[0];
}

/** Moves the computer keyboard and the screen's keys <octaves> up or down. */
function shiftKeys(octaves) {
  release();
  keyboardSaid = flyback.Shift(octaves);
  buildKeys();
  paint();
}

/** Who made the patch, and what it is tagged, by the title and above its description. */
function showCredit(author, tags) {
  const name = author?.trim() ?? '';

  ui.byline.textContent = name ? `by ${name}` : '';
  ui.byline.hidden = !name;

  ui.initial.textContent = name ? [...name][0].toUpperCase() : '';
  ui.author.textContent = name;
  ui.tags.replaceChildren(...tags.map(tag => Object.assign(document.createElement('li'), { textContent: tag })));

  // A tagged patch with no author still shows its tags.
  ui.credit.querySelector('.author').hidden = !name;
  ui.tags.hidden = tags.length === 0;
  ui.credit.hidden = !name && tags.length === 0;
}

/** The tab open on a phone's sheet, kept while the patch changes where it still has that tab. */
let tab = null;

/** Offers the tabs the open patch has a use for, and the sheet only where there is any. */
function buildSheet() {
  const has = {
    controls: knobs.length > 0,
    keys: playable(),
    // Always: on a phone it holds the size list too.
    about: true,
  };

  for (const { name, tab: button } of tabs) button.hidden = !has[name];
  if (!has[tab]) tab = tabs.find(({ name }) => has[name])?.name ?? null;

  if (has.keys) buildKeys();
  else ui.keybed.replaceChildren();

  placeSheet();
}

/** Shows the sheet as the screen's width has it: knobs beside the picture, or every tab under it. */
function placeSheet() {
  const phone = narrow.matches;

  // What the patch says it is reads under the transport, or behind a phone's About tab.
  (phone ? tabs[2].pane : ui.notesHome).append(ui.notes);

  // So does the size list, which a phone's header has no room for.
  (phone ? tabs[2].pane : document.querySelector('header')).append(ui.sizer);

  ui.sheet.hidden = phone ? tab === null : knobs.length === 0;

  for (const { name, tab: button, pane } of tabs) {
    button.setAttribute('aria-selected', String(name === tab));
    pane.classList.toggle('open', name === tab);
  }
}

function openTab(name) {
  tab = name;
  placeSheet();

  // A tab opened on a sheet closed to its tabs opens it to half the screen.
  if (ui.sheet.getBoundingClientRect().height < 120) sizePanel(SHEET_STEPS[1] * innerHeight);
}

for (const { name, tab: button } of tabs) button.onclick = () => openTab(name);
narrow.addEventListener('change', placeSheet);

/**
 * Turns <knob> to <value>, 0 to 1. The sound hears it once what is queued has played, so
 * the picture waits as long, unless <atOnce>.
 */
function turn(knob, value, atOnce = false) {
  knob.value = Math.min(Math.max(Number(value) || 0, 0), 1);
  knob.input.value = knob.value;
  knob.reading.textContent = Math.round(knob.value * 100);
  knob.draw();

  speaker.postMessage({ turn: knob.key, value: knob.value });

  const turned = ++knob.turns;
  const to = knob.value;
  const show = () => {
    // A later turn already shown wins over this one arriving late.
    if (turned < knob.shown) return;

    knob.shown = turned;
    flyback.Turn(knob.key, to);
    if (!playing) drawnAt = NaN;
  };

  const wait = !atOnce && playing && heard ? (soundStatus.queued ?? 0) * 1000 : 0;
  if (wait > 0) setTimeout(show, wait);
  else show();
}

/**
 * Opens a patch at the page's size, its picture here with <opening.picture> and its sound
 * in the worker with <opening.sound>, and starts it at <at> seconds.
 */
async function open(opening, label, at = 0, keepKnobs = false) {
  const was = playing;
  const id = ++opens;

  stop();
  opener = opening;
  warning = null;
  name = label;
  info = null;
  soundStatus = {};

  const shown = preview ? params.get('title') ?? label : label;

  pressed.clear();
  keyboardSaid = null;

  error = opening.picture(width, height);
  const sound = error === null ? await openSound(opening.sound, id) : null;

  // Another open started while this one waited on the worker, and is the one wanted.
  if (id !== opens) return;

  error ??= sound.error;

  if (error === null) {
    soundStatus = sound.status ?? {};
    info = status();
    insisted = false;
    soundAllowed = sound.status !== null;

    if (!soundAllowed) warning = `The sound could not start here, so the picture plays alone: ${speakerFailure}`;
    else if (info.hasSound && sound.speed !== null && sound.speed < FAST_ENOUGH) tooSlow(sound.speed);

    document.title = `${shown} · Flyback Viewer`;
    ui.seek.hidden = ui.loop.hidden = !Number.isFinite(end());
    if (!ui.seek.hidden) ui.seek.max = end();
  }

  ui.about.textContent = info?.description ?? '';
  ui.about.hidden = !info?.description;
  showCredit(info?.author, info?.tags ?? []);
  buildPanel(error === null, keepKnobs);
  buildSheet();

  ui.title.textContent = shown;

  seek(info === null ? 0 : Math.min(at, end()));
  ui.cover.hidden = error === null && (was || at > 0);
  say(error);

  if (was && error === null) await play();
  paint();
}

/** The cover over the picture: what went wrong, or for null the invitation to play. */
function say(text) {
  if (text) {
    ui.cover.textContent = text;
    return;
  }

  const prompt = document.createElement('span');
  const disc = document.createElement('span');
  prompt.className = 'prompt';
  disc.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true" class="solid"><path d="M7 4.5v15l13-7.5z"/></svg>';
  prompt.append(disc, matchMedia('(pointer: coarse)').matches ? 'Tap to play' : 'Click to play');
  ui.cover.replaceChildren(prompt);
}

async function openPreset(preset) {
  if (!preview) remember({ preset });
  const bytes = flyback.Pack(preset);
  const file = `${preset}.fbkb`;

  if (bytes.length === 0) {
    error = `No preset is called '${preset}'.`;
    paint();
    return;
  }

  source = { preset };

  await open({ picture: (w, h) => flyback.OpenFile(file, bytes, w, h, 'picture'), sound: { file, bytes } }, preset);
}

async function openBytes(file, bytes) {
  return open({ picture: (w, h) => flyback.OpenFile(file, bytes, w, h, 'picture'), sound: { file, bytes } }, file);
}

async function openUrl(url, name) {
  const response = await fetch(url).catch(failure => ({ ok: false, status: 0, statusText: failure.message }));
  if (!response.ok) {
    error = `${url}: ${response.status} ${response.statusText}`;
    ui.cover.hidden = false;
    ui.cover.textContent = error;
    paint();
    return;
  }

  const file = name ?? url.split('/').pop().split('?')[0];
  const bytes = new Uint8Array(await response.arrayBuffer());

  source = { url: new URL(url, location.href).href, file, bytes };
  await openBytes(file, bytes);
}

/** Opens a file dropped or picked here, which only this page has. */
async function openLocal(file) {
  const bytes = new Uint8Array(await file.arrayBuffer());

  source = { file: file.name, bytes };
  await openBytes(file.name, bytes);
}

/** The editor's address, beside this page's folder. */
const editorUrl = () => new URL('../editor/', location.href);

/**
 * Opens the patch in the editor. Back to the editor that sent it where that is still open,
 * as it was left; here for a preset or an address; in a tab of its own for bytes only this page has.
 */
function edit() {
  if (source === null) return null;

  if (params.get('from') === 'editor' && window.opener && !window.opener.closed) {
    window.opener.focus();
    window.close();
    return 'editor';
  }

  const url = editorUrl();

  if (source.preset !== undefined) {
    url.search = new URLSearchParams({ preset: source.preset });
    location.href = url.href;
    return url.href;
  }

  // A blob is its page's, so bytes only this page has go to a tab that leaves this one open.
  const here = source.url !== undefined && !source.url.startsWith('blob:');
  const asked = new URLSearchParams({ file: here ? source.url : URL.createObjectURL(new Blob([source.bytes])), name: source.file });
  if (params.has('title')) asked.set('title', params.get('title'));
  url.search = asked;

  if (here) location.href = url.href;
  else window.open(url.href, '_blank');

  return url.href;
}

/** Draws and plays at <w>×<h> from here on, carrying on from where the patch is. */
async function resize(w, h) {
  if (!(w > 0 && h > 0)) return;

  const changed = w !== width || h !== height;
  [width, height] = [w, h];
  setPicture(true);

  if (changed && opener !== null) await open(opener, name, now(), true);
}

/** Draws the picture at the page's size, or turns it off and lets the sound play alone. */
function setPicture(on) {
  pictureOn = on;
  drawnAt = NaN;

  if (on) offerSize(width, height);
  else ui.size.value = OFF;

  remember({ size: on ? `${width}x${height}` : OFF });
  showPicture();
}

/** The canvas, or in its place a line saying the picture is off. */
function showPicture() {
  ui.off.hidden = pictureOn;
  ui.canvas.style.visibility = pictureOn ? '' : 'hidden';
}

/** Picks <w>×<h> in the size list, adding it where the address asked for one the list does not offer. */
function offerSize(w, h) {
  const value = `${w}x${h}`;
  if (![...ui.size.options].some(option => option.value === value)) ui.size.add(new Option(`${w} × ${h}`, value));
  ui.size.value = value;
}

/** Keeps the address naming what is open, so it can be reloaded or passed on. */
function remember(changes) {
  const next = new URLSearchParams(location.search);
  for (const [key, value] of Object.entries(changes)) next.set(key, value);
  history.replaceState(null, '', `?${next}`);
}

function seekBy(seconds) {
  if (info === null) return;

  seek(Math.min(Math.max(now() + seconds, 0), end()));
  paint();
}

function toggleFullscreen() {
  if (document.fullscreenElement) document.exitFullscreen();
  else ui.main.requestFullscreen?.().then(turnSideways, () => {});
}

/** Turns a phone sideways for the full-screen picture; a desktop refuses, and stays as it is. */
function turnSideways() {
  screen.orientation?.lock?.('landscape').catch(() => {});
}

/** The screen's wake lock, or the request for one, while it is held. */
let awake = null;

/** Keeps a phone's screen on while the picture has all of it and plays. */
function keepAwake() {
  const wanted = playing && document.fullscreenElement != null && !document.hidden;

  if (wanted && awake === null && navigator.wakeLock) {
    const asked = awake = navigator.wakeLock.request('screen').then(lock => {
      // The browser lets go by itself when the page is hidden.
      lock.onrelease = () => { if (awake === asked) awake = null; };
      return lock;
    }, () => {
      if (awake === asked) awake = null;
      return null;
    });
  } else if (!wanted && awake !== null) {
    const held = awake;
    awake = null;
    held.then(lock => lock?.release());
  }
}

const clockText = seconds => {
  const whole = Math.max(0, Math.floor(seconds));
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`;
};

/** The header and the status line, which say where things are and what is wrong. */
function paint() {
  const ready = info !== null;

  ui.play.disabled = ui.rewind.disabled = ui.loop.disabled = ui.mute.disabled = ui.seek.disabled = !ready;
  ui.loop.setAttribute('aria-pressed', String(looped));
  ui.edit.disabled = source === null;
  if (ready && !dragging) ui.seek.value = now();
  ui.play.setAttribute('aria-label', playing ? 'Pause' : 'Play');
  ui.mute.classList.toggle('silent', !soundAllowed || muted || loudness === 0);
  ui.mute.setAttribute('aria-pressed', String(muted));
  ui.volume.value = loudness;
  ui.clock.textContent = ready ? clockText(now()) : '';
  ui.length.textContent = ready && Number.isFinite(end()) ? clockText(end()) : '';

  const said = ready ? status() : null;

  // A patch with nothing wired into its sound has nothing to turn up.
  ui.mute.hidden = ui.volume.hidden = ready && !said.hasSound;

  const parts = [];

  if (ready) {
    if (said.linking) parts.push('building the shader…');
  }

  const keyboard = ready && playable() ? keyboardSaid ?? said.keyboard : '';
  ui.keyboard.textContent = narrow.matches ? keyboard : `${keyboard} PageUp and PageDown move it`;
  ui.keyboard.hidden = !keyboard;

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
  if (info === null) return;

  let t = now();

  if (playing && t >= end()) {
    if (looped) {
      seek(0);
      t = 0;
    } else {
      pause();
      pausedAt = t = end();
    }
  }

  if (!dragging) ui.seek.value = t;
  if (noPicture !== null || !pictureOn) return;

  const dpr = window.devicePixelRatio || 1;
  const w = Math.max(1, Math.round(ui.canvas.clientWidth * dpr));
  const h = Math.max(1, Math.round(ui.canvas.clientHeight * dpr));

  if (ui.canvas.width !== w || ui.canvas.height !== h) {
    ui.canvas.width = w;
    ui.canvas.height = h;
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
ui.loop.onclick = () => setLooped(!looped);
ui.mute.onclick = toggleMute;
ui.volume.oninput = () => setVolume(Number(ui.volume.value));
ui.fullscreen.onclick = toggleFullscreen;
ui.edit.onclick = edit;
// A double click is two taps, which leave the patch as it was.
onTap(ui.canvas, () => { if (info !== null) (playing ? pause() : play()); });
ui.canvas.ondblclick = toggleFullscreen;
ui.size.onchange = () => (ui.size.value === OFF ? setPicture(false) : resize(...ui.size.value.split('x').map(Number)));

ui.find.oninput = find;
ui.resetAll.onclick = () => { for (const knob of knobs) turn(knob, knob.rest); };
ui.octaveDown.onclick = () => shiftKeys(-1);
ui.octaveUp.onclick = () => shiftKeys(1);

ui.seek.onpointerdown = () => { dragging = true; };
ui.seek.onpointerup = ui.seek.onpointercancel = () => { dragging = false; };
ui.seek.oninput = () => { seek(Number(ui.seek.value)); paint(); };

ui.file.onchange = async () => {
  const file = ui.file.files[0];
  if (file) await openLocal(file);
};

document.addEventListener('dragover', event => event.preventDefault());
document.addEventListener('drop', async event => {
  event.preventDefault();
  const file = event.dataTransfer.files[0];
  if (file) await openLocal(file);
});

document.addEventListener('keyup', event => typed(event, false));
window.addEventListener('blur', release);
document.addEventListener('visibilitychange', () => {
  if (document.hidden) release();
  keepAwake();
});
document.addEventListener('fullscreenchange', keepAwake);

document.addEventListener('keydown', event => {
  if (typed(event, true)) return;

  const target = event.target;

  // What already answers the key itself: a list, a focused button, the seek bar's arrows.
  if (target instanceof HTMLSelectElement || typing(target)) return;
  if (target instanceof HTMLButtonElement && (event.code === 'Space' || event.code === 'Enter')) return;
  if (target instanceof HTMLInputElement && target.type === 'range' && event.code.startsWith('Arrow')) return;
  if (event.ctrlKey || event.metaKey || event.altKey) return;

  switch (event.code) {
    case 'Space': event.preventDefault(); playing ? pause() : play(); break;
    case 'Home': seek(0); paint(); break;
    case 'ArrowLeft': seekBy(-5); break;
    case 'ArrowRight': seekBy(5); break;
    case 'ArrowUp': event.preventDefault(); setVolume(loudness + 0.1); break;
    case 'ArrowDown': event.preventDefault(); setVolume(loudness - 0.1); break;
    case 'KeyM': if (info !== null) toggleMute(); break;
    case 'KeyF': toggleFullscreen(); break;
  }
});

window.flyback = {
  presets: () => JSON.parse(flyback.Presets()),
  sizes: () => SIZES.map(([w, h]) => `${w}x${h}`),
  open: openPreset,
  openUrl,
  play,
  pause,
  seek: seconds => { seek(seconds); paint(); },
  size: resize,
  picture: setPicture,
  status: () => ({
    ...status(),
    name, preview, playing, looped, picture: pictureOn, awake: awake !== null, time: now(), sound: heard, soundAllowed, held, muted, volume: loudness,
    queued: soundStatus.queued ?? 0, warning, error, speakerFailure,
  }),
  loop: setLooped,
  volume: setVolume,
  strike,
  release: () => {
    pressed.clear();
    speaker.postMessage({ release: true });
  },
  panel: pixels => { sizePanel(pixels); return ui.sheet.getBoundingClientRect().height; },
  tab: name => { if (tabs.some(t => t.name === name && !t.tab.hidden)) openTab(name); return tab; },
  knobs: () => knobs.map(({ key, name: called, value, rest }) => ({ key, name: called, value, rest })),
  turn: (knob, value) => {
    const found = knobs.find(k => k.key === knob || k.name.toLowerCase() === String(knob).toLowerCase());
    if (found) turn(found, value);
    return found !== undefined;
  },
  snapshot: () => ui.canvas.toDataURL('image/png'),
  edit,
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

for (const [w, h] of SIZES) ui.size.add(new Option(`${w} × ${h}${w * 9 === h * 16 ? '' : ' (4:3)'}`, `${w}x${h}`));
ui.size.add(new Option('Picture off', OFF));
if (pictureOn) offerSize(width, height);
else ui.size.value = OFF;
showPicture();

const presets = window.flyback.presets().map(preset => preset.name);

// Back to the page that sent it here, the presets page by default, and never anywhere off this site.
const back = new URL(params.get('back') ?? 'presets.html', new URL('../', location.href));
if (back.origin === location.origin) ui.back.href = back.href;
ui.back.setAttribute('aria-label', 'Back');
ui.back.title = 'Back';

// Offered only where the site was built with the editor beside the viewer.
fetch(editorUrl(), { method: 'HEAD' }).then(response => { ui.edit.hidden = !response.ok; }, () => {});

setInterval(paint, 250);
requestAnimationFrame(frame);

if (params.has('file')) {
  await openUrl(params.get('file'), params.get('name') ?? undefined);
} else {
  const wanted = params.get('preset') ?? 'Beat you can see';
  await openPreset(presets.find(p => p.toLowerCase() === wanted.toLowerCase()) ?? presets[0]);
}

if (noPicture !== null) ui.cover.textContent = `${noPicture} The sound still plays: click to start it.`;
