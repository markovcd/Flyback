// Opens the web editor the site is about to deploy in a headless Chrome and fails unless it starts,
// so an editor that builds but cannot start never reaches the site. The Worker workflow runs it on the
// ahead-of-time build, which the gate's specs, on the interpreted build, never open.
//
//   node scripts/editor-starts.mjs <folder>...
//
// Serves the folders on loopback as one site, the first holding a path answering it, opens /editor/
// and waits for <html data-editor>, which main.js sets to "started" or "failed". The Chrome is
// FLYBACK_CHROMIUM, else chrome-headless-shell on the path.

import { spawn } from 'node:child_process';
import { createReadStream, existsSync, mkdtempSync, rmSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { extname, join, resolve, sep } from 'node:path';

/** How long the editor may take to start, downloads included; an interpreted start in CI takes seconds. */
const STARTING_MS = 180_000;

const folders = process.argv.slice(2).map(folder => resolve(folder));
if (folders.length === 0) {
  console.error('editor-starts.mjs: name the folders the site is served from');
  process.exit(2);
}

const types = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png',
  '.webp': 'image/webp', '.webmanifest': 'application/manifest+json', '.woff2': 'font/woff2',
};

/** The file a request's path names under one of the folders, or null; never anything outside them. */
function find(path) {
  const relative = decodeURIComponent(path.split('?')[0]).replace(/\/$/, '/index.html');
  for (const folder of folders) {
    const file = resolve(join(folder, relative));
    if (!file.startsWith(folder + sep)) return null;
    if (existsSync(file) && statSync(file).isFile()) return file;
  }
  return null;
}

const server = createServer((request, response) => {
  const file = find(request.url);
  if (file === null) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream' });
  createReadStream(file).pipe(response);
});
await new Promise(listening => server.listen(0, '127.0.0.1', listening));
const page = `http://127.0.0.1:${server.address().port}/editor/`;

const profile = mkdtempSync(join(tmpdir(), 'editor-starts-'));
// DevTools over a pipe (fd 3 in, fd 4 out, each message ended by a NUL) needs no WebSocket from Node.
const chrome = spawn(process.env.FLYBACK_CHROMIUM ?? 'chrome-headless-shell', [
  '--headless', '--no-sandbox', '--remote-debugging-pipe', '--enable-unsafe-swiftshader', '--no-first-run',
  '--window-size=1280,800', `--user-data-dir=${profile}`, 'about:blank',
], { stdio: ['ignore', 'ignore', 'ignore', 'pipe', 'pipe'] });

function finish(code, said) {
  (code === 0 ? console.log : console.error)(said);
  chrome.kill('SIGKILL');
  server.close();
  rmSync(profile, { recursive: true, force: true });
  process.exit(code);
}

setTimeout(() => finish(1, `The web editor did not start in ${STARTING_MS / 1000} s.`), STARTING_MS).unref();
chrome.on('exit', code => finish(1, `Chrome quit with ${code} before the web editor started.`));

let next = 0;
const waiting = new Map();
const errors = new Set();
let heard = '';
chrome.stdio[4].on('data', chunk => {
  heard += chunk;
  let end;
  while ((end = heard.indexOf('\0')) >= 0) {
    const message = JSON.parse(heard.slice(0, end));
    heard = heard.slice(end + 1);
    if (message.id !== undefined) waiting.get(message.id)?.(message);
    else if (message.method === 'Runtime.exceptionThrown') {
      const details = message.params.exceptionDetails;
      errors.add(details.exception?.description ?? details.text);
    }
  }
});

/** One DevTools command, to the page once there is a session on it, and its answer. */
const send = (method, params = {}, sessionId = undefined) => new Promise(answered => {
  const id = ++next;
  waiting.set(id, answered);
  chrome.stdio[3].write(`${JSON.stringify({ id, method, params, sessionId })}\0`);
});

const { result: { targetId } } = await send('Target.createTarget', { url: 'about:blank' });
const { result: { sessionId } } = await send('Target.attachToTarget', { targetId, flatten: true });
await send('Runtime.enable', {}, sessionId);
await send('Page.navigate', { url: page }, sessionId);

for (;;) {
  const answer = await send('Runtime.evaluate', {
    expression: `JSON.stringify([document.documentElement.dataset.editor ?? null, document.getElementById('loading')?.innerText ?? ''])`,
    returnByValue: true,
  }, sessionId);
  const [state, said] = JSON.parse(answer.result?.result?.value ?? '[null, ""]');
  if (state === 'started') finish(0, `The web editor started at ${page}`);
  if (state === 'failed') finish(1, [`The web editor did not start: ${said}`, ...errors].join('\n'));
  await new Promise(wait => setTimeout(wait, 500));
}
