// Flyback's embed for other sites. Each <pre data-flyback="Title"> on the page becomes a
// highlighted, read-only code block with Copy and Play; Play opens the web viewer in a
// frame over the page or docked on its right, as the block's data-opens says
// ("overlay", the default, or "side"). A narrow screen always gets the overlay.
//
//   <pre data-flyback="FM bell" data-opens="side">...patch text...</pre>
//   <script src="https://flybackmodular.app/embed.js" async></script>
(function () {
  "use strict";

  const script = document.currentScript;
  const origin = script && script.src ? new URL(script.src).origin : "https://flybackmodular.app";

  /** Too narrow for a panel beside the page, where the side player opens as the overlay. */
  const narrow = matchMedia("(max-width: 959px)");

  const icons = {
    mark: '<svg class="fe-mark fp-mark" viewBox="0 0 24 24" aria-hidden="true"><path d="M3 12h4l3-8 4 16 3-8h4"/></svg>',
    copy: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V5a1 1 0 0 1 1-1h9"/></svg>',
    copied: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12l5 5 9-10"/></svg>',
    play: '<svg class="solid" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 4.5v15l13-7.5z"/></svg>',
    pause: '<svg class="solid" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 4h4v16H6zM14 4h4v16h-4z"/></svg>',
    close: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12"/><path d="M18 6L6 18"/></svg>',
  };

  /** The player open now, and the block it plays. */
  let current = null;

  function make(tag, className, html) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (html) node.innerHTML = html;
    return node;
  }

  /** The block's text without the indent and blank lines an HTML page wraps it in. */
  function tidy(text) {
    const lines = text.replace(/\r\n?/g, "\n").replace(/^\s*\n/, "").replace(/\s+$/, "").split("\n");
    const indent = Math.min(...lines.filter(l => l.trim()).map(l => l.match(/^[ \t]*/)[0].length));
    return lines.map(l => l.slice(Number.isFinite(indent) ? indent : 0)).join("\n");
  }

  /** UTF-8 text as base64url, for the address's fragment, which never reaches a server. */
  function carry(text) {
    const bytes = new TextEncoder().encode(text);
    let binary = "";
    for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }

  function escape(text) {
    return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  }

  function build(pre) {
    const text = tidy(pre.textContent);
    const title = pre.getAttribute("data-flyback") || "Flyback patch";
    const side = pre.getAttribute("data-opens") === "side";

    const block = make("figure", "flyback-embed");
    block.setAttribute("aria-label", title);
    block.flyback = { text, title, side };

    const head = make("div", "fe-head", icons.mark);
    const name = make("span", "fe-title");
    name.textContent = title;
    const kind = make("span", "fe-kind");
    kind.textContent = "Flyback patch";

    const copy = make("button", "fe-copy", icons.copy);
    copy.type = "button";
    copy.title = "Copy";
    copy.setAttribute("aria-label", "Copy the code");
    copy.onclick = () => {
      navigator.clipboard.writeText(text).then(() => {
        copy.innerHTML = icons.copied;
        copy.classList.add("copied");
        setTimeout(() => {
          copy.innerHTML = icons.copy;
          copy.classList.remove("copied");
        }, 1500);
      }, () => {});
    };

    const play = make("button", "fe-play");
    play.type = "button";
    play.onclick = () => (current && current.block === block ? toggle() : open(block));

    head.append(name, kind, make("span", "fe-gap"), copy, play);

    const body = make("div", "fe-body");
    const lines = make("div", "fe-lines");
    lines.setAttribute("aria-hidden", "true");
    lines.textContent = text.split("\n").map((_, i) => i + 1).join("\n");
    const code = make("pre", "fe-code");
    code.innerHTML = window.flybackHighlight ? window.flybackHighlight(text) : escape(text);
    body.append(lines, code);

    block.append(head, body);
    block.flyback.play = play;
    block.flyback.kind = kind;
    show(block, null);

    pre.replaceWith(block);
  }

  /** The block's Play button and label as its player has it: closed, playing or paused. */
  function show(block, playing) {
    const { play, kind } = block.flyback;
    block.classList.toggle("playing", playing === true);
    kind.textContent = playing === true ? "Playing" : "Flyback patch";
    play.innerHTML = (playing === true ? icons.pause : icons.play) + (playing === true ? "Pause" : "Play");
    play.title = playing === true ? "Pause" : "Play";
    play.setAttribute("aria-label", (playing === true ? "Pause " : "Play ") + block.flyback.title);
  }

  function open(block) {
    close();

    const { text, title, side } = block.flyback;
    const docked = side && !narrow.matches;

    const player = make("div", "flyback-player " + (docked ? "side" : "overlay"));
    const box = make("div", "fp-box");
    box.setAttribute("role", "dialog");
    box.setAttribute("aria-label", title + " playing");
    if (!docked) box.setAttribute("aria-modal", "true");

    const head = make("div", "fp-head", icons.mark);
    const name = make("span", "fp-title");
    name.textContent = title;
    const shut = make("button", "fp-close", icons.close);
    shut.type = "button";
    shut.title = "Close (Esc)";
    shut.setAttribute("aria-label", "Close");
    shut.onclick = close;
    head.append(name, make("span", "fp-gap"), shut);

    const frame = document.createElement("iframe");
    frame.title = title;
    frame.allow = "autoplay; fullscreen";
    frame.src = origin + "/viewer/?embed&title=" + encodeURIComponent(title) + "#fbks=" + carry(text);

    box.append(head, frame);
    player.append(box);

    if (!docked) player.addEventListener("click", event => { if (event.target === player) close(); });

    document.body.append(player);
    document.documentElement.classList.toggle("flyback-side-open", docked);

    current = { block, player, frame };
    show(block, false);
    shut.focus();
  }

  function close() {
    if (!current) return;

    const { block, player } = current;
    current = null;
    player.remove();
    document.documentElement.classList.remove("flyback-side-open");
    show(block, null);
    block.flyback.play.focus();
  }

  function toggle() {
    current.frame.contentWindow.postMessage({ flyback: "toggle" }, origin);
  }

  window.addEventListener("message", event => {
    if (!current || event.origin !== origin || event.source !== current.frame.contentWindow) return;

    const data = event.data || {};
    if (data.flyback === "playing") show(current.block, data.playing === true);
    else if (data.flyback === "close") close();
  });

  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && current) close();
  });

  // A docked player that no longer fits becomes the overlay.
  narrow.addEventListener("change", () => {
    if (current && narrow.matches && current.player.classList.contains("side")) open(current.block);
  });

  function start() {
    const blocks = document.querySelectorAll("pre[data-flyback]");
    if (blocks.length === 0) return;

    if (!document.querySelector('link[data-flyback-embed]')) {
      const style = document.createElement("link");
      style.rel = "stylesheet";
      style.href = origin + "/assets/embed.css";
      style.setAttribute("data-flyback-embed", "");
      document.head.append(style);
    }

    const run = () => document.querySelectorAll("pre[data-flyback]").forEach(build);
    if (window.flybackHighlight) return run();

    const colors = document.createElement("script");
    colors.src = origin + "/assets/highlight.js";
    colors.onload = colors.onerror = run;
    document.head.append(colors);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
  else start();
})();
