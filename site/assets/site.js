// Colors the text language the way the app's text view does. The rules are
// Flyback.xshd's, in the same order, so a snippet here reads like the F2 view.
(function () {
  var rules = [
    ["t-com", /#[^\n]*/y],
    ["t-str", /"[^"\n]*"?/y],
    ["t-sink", /\bout\s*\.\s*\w+/y],
    ["t-key", /\b(?:let|def|group)\b/y],
    ["t-dom", /\b(?:x|y|t|radius|angle|aspect)\b(?!\s*[:(])/y],
    ["t-sock", /\b[A-Za-z_]\w*(?=\s*:)/y],
    ["t-mod", /\b[A-Za-z_][\w.]*(?=\s*\()/y],
    ["t-pipe", /\|>|<-|\.\./y],
    ["t-num", /\b\d+(?:\.\d+)?(?:ms|us|s)?\b/y],
  ];

  function escape(text) {
    return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  }

  function highlight(source) {
    var html = "";
    var plain = "";
    var i = 0;

    outer: while (i < source.length) {
      for (var r = 0; r < rules.length; r++) {
        var re = rules[r][1];
        re.lastIndex = i;
        var m = re.exec(source);
        if (m && m[0].length > 0) {
          html += escape(plain) + '<span class="' + rules[r][0] + '">' + escape(m[0]) + "</span>";
          plain = "";
          i += m[0].length;
          continue outer;
        }
      }
      // Skip a whole word at once, so a rule never starts inside one.
      var word = /\w+/y;
      word.lastIndex = i;
      var w = word.exec(source);
      var step = w ? w[0].length : 1;
      plain += source.substr(i, step);
      i += step;
    }

    return html + escape(plain);
  }

  document.querySelectorAll("code.fbks").forEach(function (el) {
    el.innerHTML = highlight(el.textContent);
  });

  // Audio players: a play button and the track's loudness drawn as bars, which
  // fill with the card's accent as the track plays and seek where clicked. The
  // bars come precomputed in data-peaks, so nothing is downloaded until play.
  // Without this script the native controls stay visible.
  var players = [];

  /** An SVG element, since createElement makes an unpainted HTML one. */
  function el(name, attrs, parent) {
    var node = document.createElementNS("http://www.w3.org/2000/svg", name);
    for (var key in attrs) node.setAttribute(key, attrs[key]);
    if (parent) parent.appendChild(node);
    return node;
  }

  function clock(seconds) {
    if (!isFinite(seconds)) seconds = 0;
    var s = Math.floor(seconds);
    return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
  }

  document.querySelectorAll(".player[data-peaks]").forEach(function (player) {
    var audio = player.querySelector("audio");
    if (!audio) return;

    var peaks = player.getAttribute("data-peaks").split(",").map(Number);
    var name = (player.closest(".track") || player).querySelector("header");
    var label = name ? name.firstChild.textContent.trim() : "track";

    var button = document.createElement("button");
    button.type = "button";
    button.className = "play";
    var playIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 2.5v11l9-5.5z" fill="currentColor"/></svg>';
    var pauseIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3.5 2.5h3v11h-3zM9.5 2.5h3v11h-3z" fill="currentColor"/></svg>';
    button.innerHTML = playIcon;
    button.setAttribute("aria-label", "Play " + label);

    var gap = 1.5, bar = 3, height = 40;
    var wave = el("svg", {
      class: "wave", viewBox: "0 0 " + peaks.length * (bar + gap) + " " + height,
      preserveAspectRatio: "none", role: "slider", tabindex: "0",
      "aria-label": "Position in " + label, "aria-valuemin": "0", "aria-valuemax": "100", "aria-valuenow": "0",
    });
    var rects = peaks.map(function (p, i) {
      var h = Math.max(2, p * height);
      return el("rect", { x: i * (bar + gap), y: (height - h) / 2, width: bar, height: h, rx: 1 }, wave);
    });

    var time = document.createElement("div");
    time.className = "time";
    time.innerHTML = "<span>0:00</span><span>1:00</span>";

    player.appendChild(button);
    player.appendChild(wave);
    player.appendChild(time);
    player.classList.add("ready");
    players.push(audio);

    function draw() {
      var fraction = audio.duration ? audio.currentTime / audio.duration : 0;
      var lit = Math.round(fraction * rects.length);
      rects.forEach(function (r, i) { r.classList.toggle("done", i < lit); });
      time.firstChild.textContent = clock(audio.currentTime);
      if (audio.duration) time.lastChild.textContent = clock(audio.duration);
      wave.setAttribute("aria-valuenow", String(Math.round(fraction * 100)));
    }

    function toggle() {
      if (audio.paused) {
        players.forEach(function (other) { if (other !== audio) other.pause(); });
        var played = audio.play();
        if (played && played.catch) played.catch(function () {});
      } else {
        audio.pause();
      }
    }

    function seek(fraction) {
      fraction = Math.min(1, Math.max(0, fraction));
      var go = function () { audio.currentTime = fraction * audio.duration; draw(); };
      if (audio.duration) go();
      else { audio.preload = "auto"; audio.addEventListener("loadedmetadata", go, { once: true }); audio.load(); }
    }

    button.addEventListener("click", toggle);
    audio.addEventListener("play", function () { button.innerHTML = pauseIcon; button.setAttribute("aria-label", "Pause " + label); });
    audio.addEventListener("pause", function () { button.innerHTML = playIcon; button.setAttribute("aria-label", "Play " + label); });
    audio.addEventListener("timeupdate", draw);
    audio.addEventListener("loadedmetadata", draw);
    audio.addEventListener("ended", draw);

    wave.addEventListener("pointerdown", function (e) {
      var box = wave.getBoundingClientRect();
      seek((e.clientX - box.left) / box.width);
    });
    wave.addEventListener("keydown", function (e) {
      if (!audio.duration) return;
      if (e.key === "ArrowRight") { audio.currentTime = Math.min(audio.duration, audio.currentTime + 5); e.preventDefault(); }
      if (e.key === "ArrowLeft") { audio.currentTime = Math.max(0, audio.currentTime - 5); e.preventDefault(); }
      if (e.key === " " || e.key === "Enter") { toggle(); e.preventDefault(); }
    });
  });

  // The gallery's clips play only while they are on screen.
  if ("IntersectionObserver" in window) {
    var reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    var watcher = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        var video = entry.target;
        if (entry.isIntersecting && !reduce) {
          var played = video.play();
          if (played && played.catch) played.catch(function () {});
        } else {
          video.pause();
        }
      });
    }, { threshold: 0.25 });

    document.querySelectorAll("video[data-autoplay]").forEach(function (v) { watcher.observe(v); });
  }
})();

// The editor itself takes the place of its photograph when asked, on a screen wide
// enough to patch on; a phone opens it in a tab. The button shows only where the
// editor is served: a site built without it answers 404 there, and elsewhere
// (Pages pointing at the preset site) the answer is opaque and taken as there.
(function () {
  document.querySelectorAll("a[data-editor]").forEach(function (link) {
    fetch(link.href, { method: "HEAD", mode: "no-cors" }).then(function (r) {
      if (r.ok || r.type === "opaque") link.hidden = false;
    }).catch(function () {});

    link.addEventListener("click", function (e) {
      if (window.innerWidth < 900) return;
      e.preventDefault();

      var frame = document.createElement("iframe");
      frame.src = link.href;
      frame.title = "The Flyback editor";
      frame.allow = "fullscreen; autoplay; microphone";
      frame.addEventListener("load", function () { frame.focus(); });

      var holder = link.parentNode;
      var photograph = [].slice.call(holder.childNodes);
      photograph.forEach(function (node) { holder.removeChild(node); });
      holder.appendChild(frame);

      // The editor's logo asks for the photograph back.
      function closed(message) {
        if (message.source !== frame.contentWindow || !message.data || message.data.flyback !== "close-editor") return;
        window.removeEventListener("message", closed);
        holder.removeChild(frame);
        photograph.forEach(function (node) { holder.appendChild(node); });
      }
      window.addEventListener("message", closed);
    });
  });
})();

// A picture opens over the page, fitted to the screen, rather than in a tab of
// its own. Pinch, wheel, double-tap or click to look closer, drag to move about,
// swipe or the arrow keys for the next one; Esc, the back button or a tap
// beside it closes it. Without this script the links still open the file.
(function () {
  var links = [].slice.call(document.querySelectorAll("a.zoom"));
  if (!links.length) return;

  function make(tag, cls, parent) {
    var node = document.createElement(tag);
    if (cls) node.className = cls;
    if (parent) parent.appendChild(node);
    return node;
  }

  var box = make("div", "lightbox", document.body);
  box.setAttribute("role", "dialog");
  box.setAttribute("aria-modal", "true");
  box.setAttribute("aria-label", "Picture");
  box.hidden = true;

  var stage = make("div", "lb-stage", box);
  var img = make("img", "lb-img", stage);
  img.alt = "";
  img.draggable = false;

  var bar = make("div", "lb-bar", box);
  var caption = make("p", "lb-caption", bar);
  var count = make("span", "lb-count", bar);
  var hint = make("p", "lb-hint", box);
  hint.textContent = "Pinch or double-tap to zoom";

  function button(cls, label, path) {
    var b = make("button", "lb-button " + cls, box);
    b.type = "button";
    b.setAttribute("aria-label", label);
    b.innerHTML = '<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="' + path + '"/></svg>';
    return b;
  }

  var close = button("lb-close", "Close", "M5 5l10 10M15 5 5 15");
  var prev = button("lb-prev", "Previous picture", "M12.5 4.5 7 10l5.5 5.5");
  var next = button("lb-next", "Next picture", "M7.5 4.5 13 10l-5.5 5.5");

  var index = -1, opener = null, pushed = false;
  var fit = { w: 0, h: 0, base: 1 };
  var view = { s: 1, x: 0, y: 0 };

  function stageSize() { return { w: stage.clientWidth, h: stage.clientHeight }; }

  function maxScale() { return Math.max(4, 2 / fit.base); }

  /** The scale a double-tap goes to: enough to fill the screen, never past the file's own pixels. */
  function closer() {
    var room = stageSize();
    var cover = Math.max(room.w / fit.w, room.h / fit.h);
    return Math.min(maxScale(), Math.max(2, Math.min(1 / fit.base, cover)));
  }

  function clamp(v, lo, hi) { return Math.min(hi, Math.max(lo, v)); }

  function place(s, x, y, animate) {
    var room = stageSize();
    s = clamp(s, 1, maxScale());
    var w = fit.w * s, h = fit.h * s;
    x = w <= room.w ? (room.w - w) / 2 : clamp(x, room.w - w, 0);
    y = h <= room.h ? (room.h - h) / 2 : clamp(y, room.h - h, 0);
    view = { s: s, x: x, y: y };
    img.classList.toggle("lb-animate", !!animate);
    img.style.transform = "translate(" + x + "px," + y + "px) scale(" + s + ")";
    box.classList.toggle("lb-zoomed", s > 1.01);
  }

  function zoomAt(s, px, py, animate) {
    var k = clamp(s, 1, maxScale()) / view.s;
    place(view.s * k, px - (px - view.x) * k, py - (py - view.y) * k, animate);
  }

  function layout() {
    var room = stageSize();
    var nw = img.naturalWidth || 16, nh = img.naturalHeight || 9;
    // A small figure is shown at its own size on a big screen; a big one is fitted.
    fit.base = Math.min(1, room.w / nw, room.h / nh);
    fit.w = nw * fit.base;
    fit.h = nh * fit.base;
    img.style.width = fit.w + "px";
    img.style.height = fit.h + "px";
    place(1, 0, 0, false);
  }

  function describe(link) {
    var figure = link.closest("figure");
    var cap = figure && figure.querySelector("figcaption");
    var inner = link.querySelector("img");
    return cap ? cap.textContent.trim() : inner ? inner.alt : "";
  }

  function show(i) {
    index = (i + links.length) % links.length;
    var link = links[index];
    var inner = link.querySelector("img");
    caption.textContent = describe(link);
    count.textContent = links.length > 1 ? index + 1 + " / " + links.length : "";
    img.alt = inner ? inner.alt : "";
    img.onload = layout;
    img.src = link.href;
    if (img.complete && img.naturalWidth) layout();
  }

  function open(i, from) {
    opener = from;
    box.hidden = false;
    document.documentElement.classList.add("lb-open");
    show(i);
    if (!pushed) { history.pushState({ lightbox: true }, ""); pushed = true; }
    close.focus({ preventScroll: true });
  }

  function hide() {
    box.hidden = true;
    document.documentElement.classList.remove("lb-open");
    img.removeAttribute("src");
    if (opener) opener.focus({ preventScroll: true });
  }

  function shut() {
    if (box.hidden) return;
    if (pushed) { pushed = false; history.back(); }
    hide();
  }

  window.addEventListener("popstate", function () {
    if (!pushed) return;
    pushed = false;
    hide();
  });

  links.forEach(function (link, i) {
    link.removeAttribute("target");
    link.addEventListener("click", function (e) {
      if (e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      open(i, link);
    });
  });

  close.addEventListener("click", shut);
  prev.addEventListener("click", function () { show(index - 1); });
  next.addEventListener("click", function () { show(index + 1); });
  if (links.length < 2) { prev.hidden = true; next.hidden = true; }

  window.addEventListener("resize", function () { if (!box.hidden) layout(); });

  document.addEventListener("keydown", function (e) {
    if (box.hidden) return;
    if (e.key === "Escape") { shut(); e.preventDefault(); }
    else if (e.key === "ArrowLeft" && view.s <= 1.01) { show(index - 1); e.preventDefault(); }
    else if (e.key === "ArrowRight" && view.s <= 1.01) { show(index + 1); e.preventDefault(); }
    else if (e.key === "+" || e.key === "=") { var r = stageSize(); zoomAt(view.s * 1.5, r.w / 2, r.h / 2, true); }
    else if (e.key === "-") { var q = stageSize(); zoomAt(view.s / 1.5, q.w / 2, q.h / 2, true); }
    else if (e.key === "Tab") {
      var stops = [close, prev, next].filter(function (b) { return !b.hidden; });
      var at = stops.indexOf(document.activeElement);
      stops[(at + (e.shiftKey ? stops.length - 1 : 1)) % stops.length].focus();
      e.preventDefault();
    }
  });

  stage.addEventListener("wheel", function (e) {
    e.preventDefault();
    var r = stage.getBoundingClientRect();
    zoomAt(view.s * Math.exp(-e.deltaY * 0.0022), e.clientX - r.left, e.clientY - r.top, false);
  }, { passive: false });

  // Pointers: one drags (or swipes, at the fitted size), two pinch.
  var pointers = new Map();
  var gesture = null, lastTap = 0;

  function local(e) {
    var r = stage.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  }

  function pair() {
    var p = Array.from(pointers.values());
    return {
      mid: { x: (p[0].x + p[1].x) / 2, y: (p[0].y + p[1].y) / 2 },
      dist: Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y) || 1,
    };
  }

  stage.addEventListener("pointerdown", function (e) {
    try { stage.setPointerCapture(e.pointerId); } catch (_) { /* a pointer already gone */ }
    pointers.set(e.pointerId, local(e));
    if (pointers.size === 1) {
      var at = local(e);
      gesture = { kind: "drag", start: at, from: { x: view.x, y: view.y }, moved: false, onImage: e.target === img, touch: e.pointerType !== "mouse" };
    } else if (pointers.size === 2) {
      var two = pair();
      gesture = { kind: "pinch", dist: two.dist, mid: two.mid, s: view.s, x: view.x, y: view.y, moved: true };
    }
  });

  stage.addEventListener("pointermove", function (e) {
    if (!pointers.has(e.pointerId) || !gesture) return;
    pointers.set(e.pointerId, local(e));

    if (gesture.kind === "pinch" && pointers.size === 2) {
      var two = pair();
      var k = clamp(gesture.s * two.dist / gesture.dist, 1, maxScale()) / gesture.s;
      place(gesture.s * k,
        two.mid.x - (gesture.mid.x - gesture.x) * k,
        two.mid.y - (gesture.mid.y - gesture.y) * k, false);
    } else if (gesture.kind === "drag") {
      var at = local(e);
      var dx = at.x - gesture.start.x, dy = at.y - gesture.start.y;
      if (Math.hypot(dx, dy) > 6) gesture.moved = true;
      if (view.s > 1.01) place(view.s, gesture.from.x + dx, gesture.from.y + dy, false);
      else if (gesture.moved) img.style.transform = "translate(" + (view.x + dx) + "px," + (view.y + Math.max(0, dy)) + "px)";
    }
  });

  function lift(e) {
    if (!pointers.has(e.pointerId)) return;
    var at = local(e);
    pointers.delete(e.pointerId);
    var g = gesture;

    if (g && g.kind === "pinch") {
      // The finger left down carries on as a drag from where it is.
      if (pointers.size === 1) {
        var rest = pointers.values().next().value;
        gesture = { kind: "drag", start: rest, from: { x: view.x, y: view.y }, moved: true, onImage: true };
      } else gesture = null;
      return;
    }

    gesture = null;
    if (!g) return;

    var dx = at.x - g.start.x, dy = at.y - g.start.y;

    if (g.moved) {
      if (view.s <= 1.01) {
        if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy)) show(index + (dx < 0 ? 1 : -1));
        else if (dy > 90 && dy > Math.abs(dx)) shut();
        else place(1, 0, 0, true);
      }
      return;
    }

    // A tap or a click.
    if (!g.onImage) { shut(); return; }

    if (g.touch) {
      var now = Date.now();
      if (now - lastTap < 320) {
        lastTap = 0;
        if (view.s > 1.01) place(1, 0, 0, true); else zoomAt(closer(), at.x, at.y, true);
      } else lastTap = now;
    } else if (view.s > 1.01) place(1, 0, 0, true);
    else zoomAt(closer(), at.x, at.y, true);
  }

  stage.addEventListener("pointerup", lift);
  stage.addEventListener("pointercancel", function (e) { pointers.delete(e.pointerId); gesture = null; place(view.s, view.x, view.y, true); });
})();

// The ad starts muted, as a clip that plays by itself must; its button turns the sound on.
(function () {
  document.querySelectorAll(".ad").forEach(function (ad) {
    var video = ad.querySelector("video");
    var button = ad.querySelector(".sound");
    if (!video || !button) return;
    var label = button.querySelector("span");

    button.addEventListener("click", function () {
      video.muted = !video.muted;
      if (!video.muted) {
        // The first time only, from the top, so the sound is heard from where it starts.
        if (!ad.hasAttribute("data-heard")) { ad.setAttribute("data-heard", ""); video.currentTime = 0; }
        var played = video.play();
        if (played && played.catch) played.catch(function () {});
      }
      button.setAttribute("aria-pressed", String(!video.muted));
      label.textContent = video.muted ? "Sound on" : "Sound off";
    });
  });
})();

// Clicking the donation code copies the address, or selects it where the
// clipboard is refused. Either way nobody retypes it.
(function () {
  document.querySelectorAll(".donate .qr").forEach(function (button) {
    var row = button.parentNode;
    var code = row.querySelector("code");
    var said = row.querySelector(".said");

    button.addEventListener("click", function () {
      var written = navigator.clipboard && navigator.clipboard.writeText(code.textContent);

      if (!written) return select();

      written.then(function () { said.textContent = "Copied."; }, select);
    });

    function select() {
      var range = document.createRange();
      range.selectNodeContents(code);
      var selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      said.textContent = "Press Ctrl+C to copy it.";
    }
  });
})();
