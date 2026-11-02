// The page around the editor: hands the runtime what it cannot reach itself (a canvas
// for the preview, WebGL on it, the speakers, the page's focus) and answers window.flyback for
// anything that wants to drive the editor without looking.

import { dotnet } from './_framework/dotnet.js';
import * as gl from '../viewer/gl.js';
import { onTap } from '../viewer/tap.js';
import * as speakers from './speakers.js';

/** The loading screen over the page, its bar and its line of text. */
const loading = document.getElementById('loading');
const loadingFill = loading.querySelector('.fill');
const loadingSaid = loading.querySelector('.said');

let loadedShare = 0;

/** How long starting may take once every file is in before the page says it is taking too long. */
const STARTING_TOO_LONG_MS = 60_000;

/**
 * Says on the loading screen why the editor did not start, so a failure never looks like a long start,
 * and marks the page for a script to read: <html data-editor> is "started" or "failed".
 */
function failed(failure) {
  if (document.documentElement.dataset.editor) return;
  document.documentElement.dataset.editor = 'failed';
  loadingSaid.textContent = `Flyback could not start: ${failure?.message ?? failure}`;
  console.error('Flyback could not start', failure);
}

/** The last error the page saw while starting, which a start that takes too long is put down to. */
let startingError = null;
const sawError = event => { startingError = event.error ?? event.message; };
const sawRejection = event => { startingError = event.reason; };
addEventListener('error', sawError);
addEventListener('unhandledrejection', sawRejection);

let tooLong = null;

/**
 * Fills the bar by files downloaded; once they all are, what is left is the runtime starting.
 * The total grows as the runtime finds more files, so the bar only ever moves forward.
 */
function downloaded(done, total) {
  loadedShare = Math.max(loadedShare, done / Math.max(total, 1));
  loadingFill.style.width = `${Math.round(100 * loadedShare)}%`;
  if (done >= total && !document.documentElement.dataset.editor) {
    loadingSaid.textContent = 'Starting';
    tooLong ??= setTimeout(() => {
      if (startingError !== null) failed(startingError);
      else if (!document.documentElement.dataset.editor) loadingSaid.textContent = 'Flyback is taking too long to start. Reloading the page may help.';
    }, STARTING_TOO_LONG_MS);
  }
}

const runtime = await dotnet
  .withModuleConfig({ onDownloadResourceProgress: downloaded })
  .create()
  .catch(failure => {
    failed(failure);
    throw failure;
  });

/** The viewer's tab, by name, so View it again replaces what it shows rather than opening another. */
const VIEWER_TAB = 'flyback-viewer';

/** The viewer's tab while it is open, and the blob it was last handed, which lives as long as this page. */
let viewerTab = null;
let viewed = null;

runtime.setModuleImports('gl', gl);
runtime.setModuleImports('speakers', speakers);
// The preview is a box holding the canvas and what is said in place of a picture it cannot draw.
runtime.setModuleImports('page', {
  createCanvas: tapped => {
    const box = document.createElement('div');
    box.style.cssText = 'position: relative; width: 100%; height: 100%;';

    const canvas = document.createElement('canvas');
    canvas.style.cssText = 'display: block; width: 100%; height: 100%;';

    const said = document.createElement('div');
    said.style.cssText = 'position: absolute; inset: 0; display: none; align-items: center; justify-content: center; '
      + 'padding: 16px; text-align: center; font: 13px/1.5 Inter, system-ui, sans-serif; color: #9a9ca3;';

    box.append(canvas, said);
    onTap(box, tapped);
    return box;
  },
  showCanvas: (box, shown) => { box.style.visibility = shown ? 'visible' : 'hidden'; },
  clipCanvas: (box, path) => { box.style.clipPath = path ? `path(evenodd, "${path}")` : ''; },
  sizeCanvas: (box, width, height) => {
    const canvas = box.firstChild;
    if (canvas.width !== width) canvas.width = width;
    if (canvas.height !== height) canvas.height = height;
  },
  sayOverCanvas: (box, text) => {
    const [canvas, said] = box.children;
    said.textContent = text ?? '';
    said.style.display = text ? 'flex' : 'none';
    canvas.style.visibility = text ? 'hidden' : '';
  },
  attachGl: box => gl.attach(box.firstChild, () => runtime.localHeapViewU8()),
  hasFocus: () => document.hasFocus(),
  onMac: () => /^mac/i.test(navigator.userAgentData?.platform ?? navigator.platform ?? ''),
  // The native controls' layer, which holds the picture's canvas, goes under Avalonia's own canvas.
  pictureBehind: behind => {
    for (const layer of document.querySelectorAll('.avalonia-native-host')) {
      // Isolated, so under Avalonia's canvas is still above the page's own ground.
      layer.parentElement.style.isolation = 'isolate';
      layer.style.zIndex = behind ? '-1' : '';
    }
  },
  // Not every browser lets a page have the screen (an iPhone's does not), so a refusal is ignored.
  wholeScreen: whole => {
    const root = document.documentElement;
    if (whole && !document.fullscreenElement) root.requestFullscreen?.().catch(() => { });
    else if (!whole && document.fullscreenElement) document.exitFullscreen?.().catch(() => { });
  },
  // A browser that keeps nothing (a private window, storage blocked) forgets the settings rather than failing.
  readSetting: section => {
    try { return localStorage.getItem(`flyback.editor.${section}`); } catch { return null; }
  },
  writeSetting: (section, json) => {
    try { localStorage.setItem(`flyback.editor.${section}`, json); } catch { }
  },
  stillsUrl: () => new URL('../stills/', location.href).href,
  // The preset site behind the page, which its <html> names; GitHub Pages has none.
  siteUrl: () => {
    const at = document.documentElement.dataset.presetSite;
    return at === undefined ? null : new URL(at, location.href).href;
  },
  // Framed by the site's own page, the editor hands the frame back rather than loading the site inside itself.
  goHome: () => {
    if (framedBySite()) {
      if (!exports.Unsaved() || confirm('Leave the editor? Your changes are not saved.')) {
        parent.postMessage({ flyback: 'close-editor' }, location.origin);
      }
      return;
    }
    location.href = new URL('../', location.href).href;
  },
  openViewerTab: () => {
    viewerTab = window.open('', VIEWER_TAB);
    return viewerTab !== null;
  },
  view: (bytes, title) => {
    if (viewed !== null) URL.revokeObjectURL(viewed);
    viewed = URL.createObjectURL(new Blob([bytes]));

    const url = new URL('../viewer/', location.href);
    url.search = new URLSearchParams({ file: viewed, name: `${title}.fbkb`, title, from: 'editor' });
    viewerTab.location.href = url.href;
    viewerTab.focus();

    return url.href;
  },
});

const name = runtime.getConfig().mainAssemblyName;
const exports = (await runtime.getAssemblyExports(name).catch(failure => { failed(failure); throw failure; }))
  .Flyback.Editor.Web.PageExports;

/** Fetches a shared preset's file and opens it; null once open, or why it was not. */
async function openUrl(url, fileName, title) {
  const response = await fetch(url).catch(failure => ({ ok: false, status: 0, statusText: failure.message }));
  if (!response.ok) return `${url}: ${response.status} ${response.statusText}`;

  const file = fileName ?? url.split('/').pop().split('?')[0];
  return await exports.Shared(title ?? file.replace(/\.[^.]+$/, ''), file, new Uint8Array(await response.arrayBuffer()));
}

// The browser's Escape ends its full screen before the editor hears the key, so the picture follows.
document.addEventListener('fullscreenchange', () => {
  if (!document.fullscreenElement) exports.ScreenLeft();
});

// The browser's own question, since nothing may wait on the editor's while the page unloads.
addEventListener('beforeunload', event => {
  if (exports.Unsaved()) event.preventDefault();
});

function framedBySite() {
  try {
    return window.parent !== window && window.parent.location.origin === location.origin;
  } catch {
    return false;
  }
}

globalThis.flyback = {
  state: () => JSON.parse(exports.State()),
  preset: preset => exports.Preset(preset),
  openUrl,
  text: () => exports.Text(),
  apply: text => exports.Apply(text),
  sound: () => speakers.status(),
  view: () => exports.View(),
};

await runtime.runMain(name, []).catch(failure => {
  failed(failure);
  throw failure;
});

document.documentElement.dataset.editor = 'started';
clearTimeout(tooLong);
removeEventListener('error', sawError);
removeEventListener('unhandledrejection', sawRejection);

// Gone once the editor's first frame is on the page beneath it.
requestAnimationFrame(() => requestAnimationFrame(() => {
  loading.classList.add('gone');
  loading.addEventListener('transitionend', () => loading.remove(), { once: true });
}));

// ?preset=<name> opens a shipped preset, and ?file=<url> a shared one, as the presets page asks.
const params = new URLSearchParams(location.search);

if (params.has('file')) await openUrl(params.get('file'), params.get('name') ?? undefined, params.get('title') ?? undefined);
else if (params.has('preset')) exports.Preset(params.get('preset'));
