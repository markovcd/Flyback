// The Cloudflare Worker on the route flyback.nasik2137.uk/*, in front of the tunnel.
// See cloudflare.md beside this file.

const PAGES = "https://markovcd.github.io/Flyback";

// Pages the GitHub site also has; anything else lands on its front page.
const ON_PAGES = /^\/(index\.html|plugins\.html|tutorials\.html|tutorials\/.*|assets\/.*)?$/;

// 502-504 is the container down; 520-530 is the tunnel down.
const DOWN = new Set([502, 503, 504, 520, 521, 522, 523, 524, 525, 526, 530]);

// The viewer's and the editor's files named with their fingerprint never change, and Cloudflare caches no .wasm on its own.
const FINGERPRINTED = /^\/(viewer|editor)\/_framework\/.+\.[a-z0-9]{10}\.[a-z]+$/;

export default {
  async fetch(request) {
    const url = new URL(request.url);

    let response = null;
    try {
      response = await fetch(request, FINGERPRINTED.test(url.pathname) ? { cf: { cacheEverything: true } } : undefined);
    } catch {}

    if (response && !DOWN.has(response.status)) return response;

    // The editor asks /api for JSON, and uploads are not pages: let them fail plainly.
    if (url.pathname.startsWith("/api/") || !["GET", "HEAD"].includes(request.method))
      return response ?? new Response("The preset site is down.", { status: 503 });

    return Response.redirect(PAGES + (ON_PAGES.test(url.pathname) ? url.pathname : "/"), 302);
  },
};
