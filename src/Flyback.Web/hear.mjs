// Plays a patch's sound through the web viewer's build, under Node, with no page:
//
//   node hear.mjs --preset "Sidebands" --seconds 2 --out sidebands.f32
//   node hear.mjs patch.fbkb --seconds 5
//
// Prints what the viewer's status says as JSON, with how fast the sound rendered, and
// writes the samples as raw 32-bit floats, left and right interleaved, when --out names a file.

import { readFile, writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';
import { dotnet } from './wwwroot/_framework/dotnet.js';

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    preset: { type: 'string' },
    seconds: { type: 'string', default: '1' },
    size: { type: 'string', default: '960x540' },
    out: { type: 'string' },
  },
});

const [width, height] = values.size.split('x').map(Number);
const runtime = await dotnet.create();
const web = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Flyback.Web.WebExports;

const file = positionals[0];
const failure = values.preset !== undefined
  ? web.OpenPreset(values.preset, width, height)
  : file !== undefined
    ? web.OpenFile(file, new Uint8Array(await readFile(file)), width, height)
    : 'Name a patch file or a --preset.';

if (failure) {
  console.error(`hear: ${failure}`);
  process.exit(1);
}

const chunk = 1024;
const status = JSON.parse(web.Status());
const frames = Math.round(Number(values.seconds) * status.sampleRate);
const sound = new Float32Array(frames * 2);

for (let at = 0; at < frames; at += chunk) {
  const count = Math.min(chunk, frames - at);
  const pointer = web.Hear(count) / 4;
  sound.set(runtime.localHeapViewF32().subarray(pointer, pointer + count * 2), at * 2);
}

if (values.out) await writeFile(values.out, new Uint8Array(sound.buffer));

console.log(web.Status());
process.exit(0);
