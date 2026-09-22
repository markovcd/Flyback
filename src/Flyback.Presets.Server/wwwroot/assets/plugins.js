// The shared plugins: a shelf of them, one plugin, the form that submits one, and the admin's review.
(function () {
  var api = "api/v1/";
  var systems = { win: "Windows", osx: "macOS", linux: "Linux", any: "any system" };
  var admin = fetch(api + "admin").then(function (r) { return r.json(); })
    .catch(function () { return { enabled: false, signedIn: false }; });

  function make(tag, attrs, text) {
    var node = document.createElement(tag);
    for (var key in attrs || {}) node.setAttribute(key, attrs[key]);
    if (text != null) node.textContent = text;
    return node;
  }

  function size(bytes) {
    return bytes < 1024 ? bytes + " B"
      : bytes < 1048576 ? Math.round(bytes / 1024) + " KB"
      : (bytes / 1048576).toFixed(1) + " MB";
  }

  function day(when) {
    return new Date(when).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  }

  function list(words) {
    return words.length < 2 ? words.join("")
      : words.slice(0, -1).join(", ") + " and " + words[words.length - 1];
  }

  /** The first few module names, and how many more. */
  function moduleNames(modules) {
    var shown = modules.slice(0, 6).map(function (m) { return m.name; });
    return modules.length > 6 ? shown.join(", ") + " and " + (modules.length - 6) + " more" : list(shown);
  }

  function builtFor(plugin) {
    return list(plugin.builds.map(function (b) { return systems[b] || b; }));
  }

  function chips(words, into, reach) {
    var row = make("div", { class: "chips" });
    words.forEach(function (word) { row.appendChild(make("span", { class: reach ? "chip reach" : "chip" }, word)); });
    into.appendChild(row);
  }

  /** Tags, each linking to the shelf of plugins that carry it. */
  function tagged(tags, into) {
    var row = make("div", { class: "chips" });
    tags.forEach(function (tag) {
      row.appendChild(make("a", { class: "chip", href: "plugins.html?tag=" + encodeURIComponent(tag) }, tag));
    });
    into.appendChild(row);
  }

  /** The plugin's own preview, linking to its page where href is given. */
  function frame(plugin, href) {
    var box = make(href ? "a" : "div", href ? { class: "frame", href: href, tabindex: "-1" } : { class: "frame" });
    box.appendChild(make("img", { src: plugin.preview, alt: "", loading: "lazy" }));
    return box;
  }

  /** Name, version and author: the lines every view of a plugin starts with. */
  function facts(plugin) {
    var row = make("div", { class: "facts" });
    row.appendChild(make("span", null, "Version " + plugin.version));
    if (plugin.author) row.appendChild(make("span", null, "by " + plugin.author));
    row.appendChild(make("span", null, builtFor(plugin)));
    return row;
  }

  // ---- review -------------------------------------------------------------

  /** Publish or unpublish, and delete; done hears "changed" or "deleted". */
  function tools(plugin, done) {
    var row = make("div", { class: "admin-tools" });

    function button(label, act, danger) {
      var node = make("button", { type: "button", class: "button small" + (danger ? " danger" : "") }, label);
      node.addEventListener("click", function () {
        var init = act();
        if (!init) return;
        node.disabled = true;
        fetch(api + "plugins/" + plugin.id, init).then(function (r) {
          if (r.status === 401) throw new Error("You are signed out.");
          if (!r.ok) throw new Error("That did not work.");
          done(init.method === "DELETE" ? "deleted" : "changed");
        }).catch(function (problem) { alert(problem.message); node.disabled = false; });
      });
      row.appendChild(node);
    }

    button(plugin.published ? "Unpublish" : "Publish", function () {
      return { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ published: !plugin.published }) };
    });
    button("Delete", function () {
      return confirm("Delete “" + plugin.name + "” for good?") ? { method: "DELETE" } : null;
    }, true);

    return row;
  }

  // ---- the shelf ----------------------------------------------------------

  function shelf() {
    var params = new URLSearchParams(location.search);
    var state = { q: params.get("q") || "", tag: params.get("tag") || "", platform: params.get("platform") || "", module: params.get("module") || "", page: +params.get("page") || 1 };
    var search = document.getElementById("search");
    var grid = document.getElementById("shelf");
    var pager = document.getElementById("pager");
    var timer = 0;
    var signed = false;

    search.value = state.q;

    function remember() {
      var query = new URLSearchParams();
      if (state.q) query.set("q", state.q);
      if (state.tag) query.set("tag", state.tag);
      if (state.platform) query.set("platform", state.platform);
      if (state.module) query.set("module", state.module);
      if (state.page > 1) query.set("page", state.page);
      var text = query.toString();
      history.replaceState(null, "", text ? "?" + text : "plugins.html");
    }

    function card(plugin) {
      var href = "plugin.html?id=" + plugin.id;
      var article = make("article", { class: "card plugin" + (plugin.published ? "" : " unpublished") });
      if (plugin.preview) article.appendChild(frame(plugin, href));

      var header = make("header");
      header.appendChild(make("a", { href: href }, plugin.name));
      if (!plugin.published) header.appendChild(make("span", { class: "badge" }, "Unpublished"));
      article.appendChild(header);

      var body = make("div", { class: "body" });
      body.appendChild(facts(plugin));
      if (plugin.description) body.appendChild(make("p", null, plugin.description));
      if (plugin.adds.length) chips(plugin.adds, body, false);
      if (plugin.modules.length) body.appendChild(make("p", { class: "modules" }, moduleNames(plugin.modules)));
      if (plugin.tags.length) tagged(plugin.tags, body);

      var foot = make("div", { class: "foot" });
      foot.appendChild(make("span", null, day(plugin.submitted)));
      foot.appendChild(make("a", { class: "button small", href: plugin.file, download: plugin.fileName }, "Download"));
      body.appendChild(foot);
      if (signed) body.appendChild(tools(plugin, load));

      article.appendChild(body);
      return article;
    }

    function load() {
      remember();
      var query = new URLSearchParams({ page: state.page });
      if (state.q) query.set("q", state.q);
      if (state.tag) query.set("tag", state.tag);
      if (state.platform) query.set("platform", state.platform);
      if (state.module) query.set("module", state.module);

      fetch(api + "plugins?" + query).then(function (r) { return r.json(); }).then(function (found) {
        grid.replaceChildren.apply(grid, found.items.map(card));
        document.getElementById("empty").hidden = found.items.length > 0;

        var pages = Math.max(1, Math.ceil(found.total / found.pageSize));
        pager.hidden = pages < 2;
        document.getElementById("where").textContent = "Page " + state.page + " of " + pages;
        document.getElementById("previous").disabled = state.page <= 1;
        document.getElementById("next").disabled = state.page >= pages;
      });
    }

    var row = document.getElementById("platforms");
    ["win", "osx", "linux"].forEach(function (platform) {
      var chip = make("button", { type: "button", class: "chip", "aria-pressed": String(platform === state.platform) }, systems[platform]);
      chip.addEventListener("click", function () {
        state.platform = state.platform === platform ? "" : platform;
        state.page = 1;
        row.querySelectorAll(".chip").forEach(function (c) { c.setAttribute("aria-pressed", "false"); });
        chip.setAttribute("aria-pressed", String(state.platform === platform));
        load();
      });
      row.appendChild(chip);
    });

    if (state.tag) {
      var tag = make("button", { type: "button", class: "chip", "aria-pressed": "true", title: "Show every tag" }, state.tag + " ×");
      tag.addEventListener("click", function () { state.tag = ""; state.page = 1; tag.remove(); load(); });
      row.appendChild(tag);
    }

    search.addEventListener("input", function () {
      clearTimeout(timer);
      timer = setTimeout(function () { state.q = search.value.trim(); state.page = 1; load(); }, 250);
    });
    document.getElementById("previous").addEventListener("click", function () { state.page--; load(); scrollTo(0, 0); });
    document.getElementById("next").addEventListener("click", function () { state.page++; load(); scrollTo(0, 0); });

    admin.then(function (state) {
      signed = state.signedIn;
      if (signed) document.querySelector(".shelf-head .actions")
        .appendChild(make("a", { class: "button", href: "admin.html" }, "Admin"));
      load();
    });
  }

  // ---- one plugin ---------------------------------------------------------

  /** Everything the install dialog will show, before the package is downloaded. */
  function described(plugin, into) {
    var dl = make("dl", { class: "said" });

    function row(term, value) {
      dl.appendChild(make("dt", null, term));
      var dd = make("dd");
      if (typeof value === "string") dd.textContent = value;
      else dd.appendChild(value);
      dl.appendChild(dd);
    }

    if (plugin.tags.length) {
      var tags = make("div");
      tagged(plugin.tags, tags);
      row("Tags", tags);
    }

    var adds = make("div");
    if (plugin.adds.length) chips(plugin.adds, adds, false);
    else adds.textContent = "Nothing it registers";
    row("Adds", adds);

    if (plugin.modules.length) {
      var modules = make("div", { class: "chips" });
      plugin.modules.forEach(function (m) { modules.appendChild(make("span", { class: "chip", title: m.id }, m.name)); });
      row("Modules", modules);
    } else if (plugin.adds.indexOf("modules") >= 0) {
      row("Modules", "Not listed: it was built before a plugin declared them");
    }

    var reaches = make("div");
    if (plugin.reaches.length) chips(plugin.reaches, reaches, true);
    else reaches.textContent = "Nothing outside Flyback that its code names";
    row("Reaches", reaches);

    row("Built for", builtFor(plugin));

    var contract = Object.keys(plugin.contract).sort().map(function (name) { return name + " " + plugin.contract[name]; });
    if (contract.length) row("Built against", contract.join(", "));

    row("Assembly", plugin.assembly);
    row("SHA-256", make("code", { class: "hash" }, plugin.sha256));

    into.appendChild(dl);
  }

  function one() {
    var id = new URLSearchParams(location.search).get("id");
    var page = document.getElementById("plugin");
    var signed = false;

    admin.then(function (state) {
      signed = state.signedIn;
      return fetch(api + "plugins/" + encodeURIComponent(id || ""));
    }).then(function (r) {
      if (!r.ok) throw new Error();
      return r.json();
    }).then(function (plugin) {
      document.title = plugin.name + " — Flyback";

      var text = make("div");
      text.appendChild(make("span", { class: "eyebrow" }, "Plugin"));
      text.appendChild(make("h1", null, plugin.name));
      if (plugin.description) text.appendChild(make("p", { class: "lede" }, plugin.description));

      var more = facts(plugin);
      more.appendChild(make("span", null, day(plugin.submitted)));
      more.appendChild(make("span", null, size(plugin.size)));
      more.appendChild(make("span", null, plugin.downloads + (plugin.downloads === 1 ? " download" : " downloads")));
      text.appendChild(more);

      var actions = make("div", { class: "actions" });
      actions.appendChild(make("a", { class: "button primary", href: plugin.file, download: plugin.fileName }, "Download"));
      actions.appendChild(make("a", { class: "button", href: "plugins.html" }, "All plugins"));
      text.appendChild(actions);

      if (!plugin.published) text.appendChild(make("span", { class: "badge" }, "Unpublished"));
      if (signed) text.appendChild(tools(plugin, function (what) {
        if (what === "deleted") location.href = "plugins.html";
        else location.reload();
      }));

      var card = make("article", { class: "card" });
      card.appendChild(make("header", null, "What it says it does"));
      var body = make("div", { class: "body" });
      described(plugin, body);
      body.appendChild(make("p", { class: "quiet" },
        "Read from the package without running it. Flyback shows the same when the package is opened, and installs nothing until asked."));
      card.appendChild(body);

      var side = make("div", { class: "side" });
      if (plugin.preview) side.appendChild(frame(plugin));
      side.appendChild(card);

      page.replaceChildren(text, side);
    }).catch(function () {
      page.replaceChildren(make("p", { class: "empty" }, "There is no such plugin."));
    });
  }

  // ---- submitting ---------------------------------------------------------

  function submit() {
    var form = document.getElementById("submit");
    var file = document.getElementById("file");
    var status = document.getElementById("status");
    var received = document.getElementById("received");

    function tell(text, bad) {
      status.textContent = text;
      status.classList.toggle("bad", !!bad);
    }

    file.addEventListener("change", function () {
      tell("");
      var chosen = file.files[0];
      if (chosen && !/\.fbkp$/i.test(chosen.name)) tell("That is not a plugin package. Send a .fbkp file.", true);
      else if (chosen && chosen.size > 64 * 1048576) tell("That package is too large.", true);
    });

    form.addEventListener("submit", function (e) {
      e.preventDefault();
      var send = document.getElementById("send");
      send.disabled = true;
      received.hidden = true;
      tell("Sending…");

      fetch(api + "plugins", { method: "POST", body: new FormData(form) }).then(function (r) {
        if (r.status === 429) throw new Error("That is a lot of submissions at once. Try again in an hour.");
        if (r.status === 413) throw new Error("That package is too large.");
        return r.json().then(function (answer) {
          if (!r.ok) throw new Error(answer.error || "The plugin was not taken.");

          tell("Received. " + answer.name + " is listed once it has been reviewed.");
          var card = make("article", { class: "card" });
          card.appendChild(make("header", null, answer.name + " " + answer.version));
          var body = make("div", { class: "body" });
          if (answer.preview) body.appendChild(frame(answer));
          if (answer.description) body.appendChild(make("p", null, answer.description));
          described(answer, body);
          card.appendChild(body);
          received.replaceChildren(card);
          received.hidden = false;
          form.reset();
          send.disabled = false;
        });
      }).catch(function (problem) {
        tell(problem.message || "The plugin was not taken.", true);
        send.disabled = false;
      });
    });
  }

  var which = document.body.getAttribute("data-page");
  if (which === "plugins") shelf();
  else if (which === "plugin") one();
  else if (which === "submit-plugin") submit();
})();
