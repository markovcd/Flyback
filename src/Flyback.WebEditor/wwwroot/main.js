// The page around the editor: hands the runtime what it cannot reach itself (a canvas
// for the preview, WebGL on it, the speakers, the page's focus) and answers window.flyback for
// anything that wants to drive the editor without looking.

import { dotnet } from './_framework/dotnet.js';
import * as gl from '../viewer/gl.js';
import * as speakers from './speakers.js';

const runtime = await dotnet.create();

runtime.setModuleImports('gl', gl);
runtime.setModuleImports('speakers', speakers);
runtime.setModuleImports('page', {
  createCanvas: () => {
    const canvas = document.createElement('canvas');
    canvas.style.width = '100%';
    canvas.style.height = '100%';
    canvas.style.display = 'block';
    return canvas;
  },
  showCanvas: (canvas, shown) => { canvas.style.visibility = shown ? 'visible' : 'hidden'; },
  clipCanvas: (canvas, path) => { canvas.style.clipPath = path ? `path(evenodd, "${path}")` : ''; },
  sizeCanvas: (canvas, width, height) => {
    if (canvas.width !== width) canvas.width = width;
    if (canvas.height !== height) canvas.height = height;
  },
  attachGl: canvas => gl.attach(canvas, () => runtime.localHeapViewU8()),
  hasFocus: () => document.hasFocus(),
  stillsUrl: () => new URL('../stills/', location.href).href,
});

const name = runtime.getConfig().mainAssemblyName;
const exports = (await runtime.getAssemblyExports(name)).Flyback.WebEditor.PageExports;

/** Fetches a shared preset's file and opens it; null once open, or why it was not. */
async function openUrl(url, fileName, title) {
  const response = await fetch(url).catch(failure => ({ ok: false, status: 0, statusText: failure.message }));
  if (!response.ok) return `${url}: ${response.status} ${response.statusText}`;

  const file = fileName ?? url.split('/').pop().split('?')[0];
  return await exports.Shared(title ?? file.replace(/\.[^.]+$/, ''), file, new Uint8Array(await response.arrayBuffer()));
}

globalThis.flyback = {
  state: () => JSON.parse(exports.State()),
  preset: preset => exports.Preset(preset),
  openUrl,
  text: () => exports.Text(),
  apply: text => exports.Apply(text),
  sound: () => speakers.status(),
};

await runtime.runMain(name, []);

// ?preset=<name> opens a shipped preset, and ?file=<url> a shared one, as the presets page asks.
const params = new URLSearchParams(location.search);

if (params.has('file')) await openUrl(params.get('file'), params.get('name') ?? undefined, params.get('title') ?? undefined);
else if (params.has('preset')) exports.Preset(params.get('preset'));
