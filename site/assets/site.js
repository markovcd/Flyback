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

  // Patch diagrams, drawn with the canvas's geometry: a header, one row per
  // socket with the outputs first, outputs on the right edge and inputs on the
  // left. A figure carries its patch as JSON in data-patch:
  //   { nodes: [{ id, name, cat, x, y, w?, outs: [name | [name, kind]],
  //               ins: [name | [name, value?, kind?]] }],
  //     wires: [[fromId, outIndex, toId, inIndex]] }
  var SVG = "http://www.w3.org/2000/svg";
  var HEADER = 22, ROW = 17, PAD = 6, WIDTH = 120;
  var accents = {
    output: "--sink", sources: "--source", oscillators: "--oscillator", timing: "--sequencer",
    maths: "--maths", geometry: "--space", patterns: "--pattern", color: "--tint",
    feedback: "--feedback", forms: "--form", pitch: "--note", shaping: "--shaping",
    echo: "--echo", measurement: "--reading",
  };
  var ports = { color: "--port-color", any: "--port-any", scalar: "--port-scalar" };

  function el(name, attrs, parent) {
    var node = document.createElementNS(SVG, name);
    for (var key in attrs) node.setAttribute(key, attrs[key]);
    if (parent) parent.appendChild(node);
    return node;
  }

  function text(parent, x, y, value, style, anchor) {
    var t = el("text", { x: x, y: y, style: style, "text-anchor": anchor || "start", "dominant-baseline": "central" }, parent);
    t.textContent = value;
    return t;
  }

  function spec(entry) {
    return Array.isArray(entry) ? entry : [entry];
  }

  function drawPatch(figure) {
    var patch;
    try { patch = JSON.parse(figure.getAttribute("data-patch")); } catch (e) { return; }

    var byId = {};
    var right = 0, bottom = 0;

    patch.nodes.forEach(function (n) {
      n.w = n.w || WIDTH;
      n.h = HEADER + (n.outs.length + n.ins.length) * ROW + PAD;
      byId[n.id] = n;
      right = Math.max(right, n.x + n.w);
      bottom = Math.max(bottom, n.y + n.h);
    });

    var svg = el("svg", {
      viewBox: "0 0 " + (right + 10) + " " + (bottom + 10),
      role: "img",
      "aria-label": figure.getAttribute("data-label") || "A patch diagram",
      style: "font-family: var(--sans); width: 100%; max-width: " + Math.round((right + 10) * 1.3) + "px; height: auto; display: block;",
    });

    var wires = el("g", { fill: "none", "stroke-width": "1.6" }, svg);
    var nodes = el("g", {}, svg);

    function outPoint(n, i) { return [n.x + n.w, n.y + HEADER + (i + 0.5) * ROW]; }
    function inPoint(n, i) { return [n.x, n.y + HEADER + (n.outs.length + i + 0.5) * ROW]; }

    patch.wires.forEach(function (w) {
      var from = byId[w[0]], to = byId[w[2]];
      if (!from || !to) return;
      var a = outPoint(from, w[1]), b = inPoint(to, w[3]);
      var reach = Math.max(30, Math.abs(b[0] - a[0]) * 0.5);
      var kind = spec(from.outs[w[1]])[1] || "scalar";
      el("path", {
        d: "M" + a[0] + " " + a[1] + " C" + (a[0] + reach) + " " + a[1] + " " + (b[0] - reach) + " " + b[1] + " " + b[0] + " " + b[1],
        style: "stroke: var(" + ports[kind] + "); opacity: 0.85;",
      }, wires);
    });

    patch.nodes.forEach(function (n) {
      var g = el("g", {}, nodes);
      var accent = "var(" + (accents[n.cat] || "--maths") + ")";
      el("rect", { x: n.x, y: n.y, width: n.w, height: n.h, rx: 6, style: "fill: var(--node); stroke: var(--edge); stroke-width: 1.5;" }, g);
      el("path", {
        d: "M" + n.x + " " + (n.y + HEADER) + " V" + (n.y + 6) + " a6 6 0 0 1 6 -6 H" + (n.x + n.w - 6) + " a6 6 0 0 1 6 6 V" + (n.y + HEADER) + " Z",
        style: "fill: " + accent + ";",
      }, g);
      text(g, n.x + 8, n.y + HEADER / 2 + 0.5, n.name, "fill: #fff; font-size: 11px; font-weight: 600;");

      n.outs.forEach(function (o, i) {
        var s = spec(o), p = outPoint(n, i);
        text(g, p[0] - 10, p[1], s[0], "fill: var(--label); font-size: 10px;", "end");
        el("circle", { cx: p[0], cy: p[1], r: 3.6, style: "fill: var(" + ports[s[1] || "scalar"] + "); stroke: var(--outline); stroke-width: 1.2;" }, g);
      });

      n.ins.forEach(function (input, i) {
        var s = spec(input), p = inPoint(n, i);
        text(g, p[0] + 10, p[1], s[0], "fill: var(--label); font-size: 10px;");
        if (s[1] !== undefined && s[1] !== null && s[1] !== "") {
          text(g, n.x + n.w - 8, p[1], String(s[1]), "fill: var(--value); font-size: 9.5px;", "end");
        }
        el("circle", { cx: p[0], cy: p[1], r: 3.6, style: "fill: var(" + ports[s[2] || "scalar"] + "); stroke: var(--outline); stroke-width: 1.2;" }, g);
      });
    });

    figure.insertBefore(svg, figure.firstChild);
  }

  document.querySelectorAll("[data-patch]").forEach(drawPatch);

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
