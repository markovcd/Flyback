// Colors the text language the way the app's code view does. The rules are
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
