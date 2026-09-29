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

globalThis.flyback = {
  state: () => JSON.parse(exports.State()),
  preset: preset => exports.Preset(preset),
  text: () => exports.Text(),
  apply: text => exports.Apply(text),
  sound: () => speakers.status(),
};

await runtime.runMain(name, []);

// ?preset=<name> opens that shipped preset, as a page embedding the editor asks for.
const preset = new URLSearchParams(location.search).get('preset');
if (preset) exports.Preset(preset);
