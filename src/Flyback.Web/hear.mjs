// Plays a patch's sound through the web viewer's build, under Node, with no page:
//
//   node hear.mjs --preset "Sidebands" --seconds 2 --out sidebands.f32
//   node hear.mjs patch.fbkb --seconds 5
//   node hear.mjs --preset "Vigil" --knob hall=0.9 --knob fog=0
//   node hear.mjs --preset "Played" --note 60:0.1:0.6 --note 64:0.3:0.6
//   node hear.mjs --edit 0:before.fbk --edit 0.5:after.fbk
//   node hear.mjs --presets
//   node hear.mjs --preset "Sidebands" --oversample 1
//   node hear.mjs --preset "Duck" --seconds 1 --state duck.state
//
// Prints what the viewer's status says as JSON, with how fast the sound rendered and the
// panel's knobs, and writes the samples as raw 32-bit floats, left and right interleaved,
// when --out names a file. --knob turns a knob, by name, 0 to 1, before anything plays.
// --note holds a note on the computer keyboard from one second to another, struck at the
// first buffer of 1,024 frames that starts at or after each. --edit hands over a patch
// file written as JSON as the web editor does, at the first such buffer at or after its
// second; one at 0 stands for a preset or a file.
// --oversample works the sound out at 1, 2 or 4 times the output rate, as the page does
// once it has stepped down; left out, the default.
// --state writes what the sound hands the picture once it has played, as the page's worker
// packs it: the Meters' readings, then each Scope's and Analyzer's buffer, as raw floats.
// With --presets it prints the viewer's preset list as JSON instead, and plays nothing.

import { existsSync } from 'node:fs';
import { readFile, writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';
import * as program from './program.js';

// A build keeps the runtime at wwwroot/_framework; a publish puts it under the path the site serves it at.
const runtimeAt = ['./wwwroot/viewer/_framework/dotnet.js', './wwwroot/_framework/dotnet.js']
  .map(path => new URL(path, import.meta.url))
  .find(url => existsSync(url));

const { dotnet } = await import(runtimeAt.href);

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    preset: { type: 'string' },
    seconds: { type: 'string', default: '1' },
    size: { type: 'string', default: '960x540' },
    out: { type: 'string' },
    state: { type: 'string' },
    oversample: { type: 'string' },
    presets: { type: 'boolean' },
    knob: { type: 'string', multiple: true, default: [] },
    note: { type: 'string', multiple: true, default: [] },
    edit: { type: 'string', multiple: true, default: [] },
  },
});

const [width, height] = values.size.split('x').map(Number);
const runtime = await dotnet.create();
runtime.setModuleImports('program', program);
program.attach({
  f32: () => runtime.localHeapViewF32(),
  f64: () => runtime.localHeapViewF64(),
  i32: () => runtime.localHeapViewI32(),
  u8: () => runtime.localHeapViewU8(),
});
const web = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Flyback.Web.WebExports;

if (values.presets) {
  console.log(web.Presets());
  process.exit(0);
}

const edits = await Promise.all(values.edit.map(async given => {
  const colon = given.indexOf(':');
  const seconds = Number(given.slice(0, colon));

  if (colon < 0 || !Number.isFinite(seconds)) {
    console.error(`hear: --edit ${given}: write it seconds:file, as 0.5:after.fbk`);
    process.exit(1);
  }

  return { seconds, text: await readFile(given.slice(colon + 1), 'utf8') };
}));

edits.sort((a, b) => a.seconds - b.seconds);

/** Hands over the next edit, as the web editor's worker takes it. */
function edit() {
  const failed = web.Edit(edits.shift().text, width / height);

  if (failed) {
    console.error(`hear: ${failed}`);
    process.exit(1);
  }
}

// A preset is packed and opened as its bundle, as the page does.
const packed = values.preset !== undefined ? web.Pack(values.preset) : null;
const file = positionals[0];
const opening = edits.length > 0 && edits[0].seconds <= 0;
if (opening) edit();

const failure = opening ? null : packed !== null
  ? packed.length > 0 ? web.OpenFile(`${values.preset}.fbkb`, packed, width, height, 'sound') : `No preset is called '${values.preset}'.`
  : file !== undefined
    ? web.OpenFile(file, new Uint8Array(await readFile(file)), width, height, 'sound')
    : 'Name a patch file or a --preset.';

if (failure) {
  console.error(`hear: ${failure}`);
  process.exit(1);
}

if (values.oversample !== undefined) web.Oversample(Number(values.oversample));

const knobs = JSON.parse(web.Knobs());

for (const turned of values.knob) {
  const [called, value] = turned.split('=');
  const knob = knobs.find(k => k.name.toLowerCase() === called.trim().toLowerCase() || k.key === called.trim());

  if (knob === undefined || value === undefined || !Number.isFinite(Number(value))) {
    console.error(`hear: --knob ${turned}: name one of ${knobs.map(k => k.name).join(', ') || 'no knobs'}, as name=0.5`);
    process.exit(1);
  }

  web.Turn(knob.key, Number(value));
  knob.value = Math.min(Math.max(Number(value), 0), 1);
}

const chunk = 1024;
const status = JSON.parse(web.Status());
const frames = Math.round(Number(values.seconds) * status.sampleRate);
const sound = new Float32Array(frames * 2);

const strikes = values.note.flatMap(held => {
  const [note, from, to] = held.split(':').map(Number);

  if (![note, from, to].every(Number.isFinite)) {
    console.error(`hear: --note ${held}: write it note:from:to, as 60:0.1:0.6`);
    process.exit(1);
  }

  return [
    { at: Math.round(from * status.sampleRate), note, down: true },
    { at: Math.round(to * status.sampleRate), note, down: false },
  ];
}).sort((a, b) => a.at - b.at);

for (const later of edits) later.at = Math.round(later.seconds * status.sampleRate);

for (let at = 0; at < frames; at += chunk) {
  while (edits.length > 0 && edits[0].at <= at) edit();

  while (strikes.length > 0 && strikes[0].at <= at) {
    const { note, down } = strikes.shift();
    web.Strike(note, down);
  }

  const count = Math.min(chunk, frames - at);
  const pointer = web.Hear(count) / 4;
  sound.set(runtime.localHeapViewF32().subarray(pointer, pointer + count * 2), at * 2);
}

if (values.out) await writeFile(values.out, new Uint8Array(sound.buffer));

if (values.state) {
  const at = web.Listen() / 4;
  const length = JSON.parse(web.Status()).stateLength;
  const state = at === 0 ? new Float32Array(0) : runtime.localHeapViewF32().slice(at, at + length);
  await writeFile(values.state, new Uint8Array(state.buffer));
}

console.log(JSON.stringify({ ...JSON.parse(web.Status()), knobs }));
process.exit(0);
