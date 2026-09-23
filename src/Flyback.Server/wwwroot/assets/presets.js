// The preset site: a shelf of presets, one preset, the form that submits one, and admin sign-in, reports and letters.
(function () {
  var api = "api/v1/";

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

  /** The still, with the loop playing over it while pointed at. */
  function frame(preset, href) {
    var box = make(href ? "a" : "div", href ? { class: "frame", href: href, tabindex: "-1" } : { class: "frame" });
    var media = preset.media;

    if (media.still) box.appendChild(make("img", { src: media.still, alt: "", loading: "lazy" }));
    else box.appendChild(make("span", { class: "waiting" },
      media.state === "failed" ? "No picture" : media.state === "done" ? "Sound only" : "Rendering soon"));

    if (media.loop) {
      var video = null;
      var start = function () {
        if (!video) {
          video = make("video", { src: media.loop, muted: "", loop: "", playsinline: "", preload: "auto" });
          video.muted = true;
          box.appendChild(video);
        }
        var played = video.play();
        if (played && played.catch) played.catch(function () {});
      };
      var stop = function () { if (video) video.pause(); };
      if (href) {
        box.addEventListener("mouseenter", start);
        box.addEventListener("mouseleave", stop);
      } else {
        start();
      }
    }

    return box;
  }

  function chips(tags, into, link) {
    var row = make("div", { class: "chips" });
    tags.forEach(function (tag) {
      row.appendChild(link ? make("a", { class: "chip", href: "./?tag=" + encodeURIComponent(tag) }, tag)
                           : make("span", { class: "chip" }, tag));
    });
    into.appendChild(row);
  }

  // ---- admin tools ----------------------------------------------------------

  function change(preset, body) {
    return fetch(api + "presets/" + preset.id, body
      ? { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }
      : { method: "DELETE" }).then(function (r) {
      if (r.status === 401) throw new Error("You are signed out.");
      if (!r.ok) throw new Error("That did not work.");
      return body ? "changed" : "deleted";
    });
  }

  /** Rename, unpublish or publish, and delete; done hears "changed" or "deleted". */
  function tools(preset, done) {
    var row = make("div", { class: "admin-tools" });

    function button(label, act, danger) {
      var node = make("button", { type: "button", class: "button small" + (danger ? " danger" : "") }, label);
      node.addEventListener("click", function () {
        var doing = act();
        if (!doing) return;
        node.disabled = true;
        doing.then(done, function (problem) { alert(problem.message); node.disabled = false; });
      });
      row.appendChild(node);
    }

    button("Rename", function () {
      var name = prompt("Rename the preset", preset.name);
      return name && name.trim() && name !== preset.name ? change(preset, { name: name }) : null;
    });
    button(preset.published ? "Unpublish" : "Publish", function () {
      return change(preset, { published: !preset.published });
    });
    button("Delete", function () {
      return confirm("Delete \u201C" + preset.name + "\u201D for good?") ? change(preset, null) : null;
    }, true);

    return row;
  }

  // ---- the shelf ----------------------------------------------------------

  function shelf() {
    var params = new URLSearchParams(location.search);
    var state = { q: params.get("q") || "", tag: params.get("tag") || "", page: +params.get("page") || 1 };
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
      if (state.page > 1) query.set("page", state.page);
      var text = query.toString();
      history.replaceState(null, "", text ? "?" + text : "./");
    }

    function card(preset) {
      var href = "preset.html?id=" + preset.id;
      var article = make("article", { class: "card preset" + (preset.published ? "" : " unpublished") });
      article.appendChild(frame(preset, href));

      var header = make("header");
      header.appendChild(make("a", { href: href }, preset.name));
      if (!preset.published) header.appendChild(make("span", { class: "badge" }, "Unpublished"));
      article.appendChild(header);

      var body = make("div", { class: "body" });
      if (preset.author) body.appendChild(make("span", { class: "by" }, "by " + preset.author));
      body.appendChild(FlybackRatings.stars(preset.rating));
      if (preset.description) body.appendChild(make("p", null, preset.description));
      if (preset.tags.length) chips(preset.tags, body, true);

      var foot = make("div", { class: "foot" });
      foot.appendChild(make("span", null, day(preset.submitted)));
      foot.appendChild(make("a", { class: "button small", href: preset.file, download: preset.fileName }, "Download"));
      body.appendChild(foot);

      if (signed) body.appendChild(tools(preset, function () { tags(); load(); }));

      article.appendChild(body);
      return article;
    }

    function load() {
      remember();
      var query = new URLSearchParams({ page: state.page });
      if (state.q) query.set("q", state.q);
      if (state.tag) query.set("tag", state.tag);

      fetch(api + "presets?" + query).then(function (r) { return r.json(); }).then(function (found) {
        grid.replaceChildren.apply(grid, found.items.map(card));
        document.getElementById("empty").hidden = found.items.length > 0;

        var pages = Math.max(1, Math.ceil(found.total / found.pageSize));
        pager.hidden = pages < 2;
        document.getElementById("where").textContent = "Page " + state.page + " of " + pages;
        document.getElementById("previous").disabled = state.page <= 1;
        document.getElementById("next").disabled = state.page >= pages;
      });
    }

    function tags() {
      fetch(api + "tags").then(function (r) { return r.json(); }).then(function (all) {
        var row = document.getElementById("tags");
        row.replaceChildren();
        all.forEach(function (t) {
          var chip = make("button", { type: "button", class: "chip", "aria-pressed": String(t.tag === state.tag) }, t.tag);
          chip.appendChild(make("small", null, String(t.count)));
          chip.addEventListener("click", function () {
            state.tag = state.tag === t.tag ? "" : t.tag;
            state.page = 1;
            row.querySelectorAll(".chip").forEach(function (c) {
              c.setAttribute("aria-pressed", String(c.firstChild.textContent === state.tag));
            });
            load();
          });
          row.appendChild(chip);
        });
      });
    }

    search.addEventListener("input", function () {
      clearTimeout(timer);
      timer = setTimeout(function () { state.q = search.value.trim(); state.page = 1; load(); }, 250);
    });
    document.getElementById("previous").addEventListener("click", function () { state.page--; load(); scrollTo(0, 0); });
    document.getElementById("next").addEventListener("click", function () { state.page++; load(); scrollTo(0, 0); });

    admin.then(function (state) {
      signed = state.signedIn;
      tags();
      load();
    });
  }

  // ---- one preset -----------------------------------------------------------

  function one() {
    var id = new URLSearchParams(location.search).get("id");
    var page = document.getElementById("preset");
    var signed = false;

    admin.then(function (state) {
      signed = state.signedIn;
      return fetch(api + "presets/" + encodeURIComponent(id || ""));
    }).then(function (r) {
      if (!r.ok) throw new Error();
      return r.json();
    }).then(function (preset) {
      document.title = preset.name + " — Flyback Presets";

      var picture = make("div");
      picture.appendChild(frame(preset, null));

      var text = make("div");
      text.appendChild(make("span", { class: "eyebrow" }, "Preset"));
      text.appendChild(make("h1", null, preset.name));
      if (!preset.published) text.appendChild(make("span", { class: "badge" }, "Unpublished"));
      if (preset.description) text.appendChild(make("p", { class: "lede" }, preset.description));

      var facts = make("div", { class: "facts" });
      if (preset.author) facts.appendChild(make("span", null, "by " + preset.author));
      facts.appendChild(make("span", null, day(preset.submitted)));
      facts.appendChild(make("span", null, size(preset.size)));
      facts.appendChild(make("span", null, preset.downloads + (preset.downloads === 1 ? " download" : " downloads")));
      text.appendChild(facts);
      if (preset.published) text.appendChild(FlybackRatings.widget("preset", preset.id));

      if (preset.tags.length) {
        var tagged = make("div", { style: "margin-top: 16px" });
        chips(preset.tags, tagged, true);
        text.appendChild(tagged);
      }

      var actions = make("div", { class: "actions" });
      actions.appendChild(make("a", { class: "button primary", href: preset.file, download: preset.fileName }, "Download"));
      actions.appendChild(make("a", { class: "button", href: "./" }, "All presets"));
      text.appendChild(actions);

      if (signed) text.appendChild(tools(preset, function (what) {
        if (what === "deleted") location.href = "./";
        else location.reload();
      }));
      else text.appendChild(FlybackReports.form("preset", preset.id, preset.name));

      if (preset.media.audio && preset.media.peaks) {
        var track = make("article", { class: "card track" });
        track.appendChild(make("header", null, "Listen"));
        var player = make("div", { class: "player", "data-peaks": preset.media.peaks.join(",") });
        player.appendChild(make("audio", { controls: "", preload: "none", src: preset.media.audio }));
        var body = make("div", { class: "body" });
        body.appendChild(player);
        track.appendChild(body);
        text.appendChild(track);
      }

      page.replaceChildren(picture, text);
      document.body.appendChild(make("script", { src: "assets/site.js" }));
    }).catch(function () {
      page.replaceChildren(make("p", { class: "empty" }, "There is no such preset."));
    });
  }

  // ---- submitting -----------------------------------------------------------

  function submit() {
    var form = document.getElementById("submit");
    var file = document.getElementById("file");
    var status = document.getElementById("status");
    var said = document.getElementById("said");

    function tell(text, bad) {
      status.textContent = text;
      status.classList.toggle("bad", !!bad);
    }

    function show(id, value) {
      var node = document.getElementById(id);
      node.textContent = value || "Nothing said";
      node.classList.toggle("quiet", !value);
    }

    file.addEventListener("change", function () {
      said.hidden = true;
      tell("");
      var chosen = file.files[0];
      if (!chosen) return;

      if (/\.fbkb$/i.test(chosen.name)) {
        tell("A bundle is read when it arrives, so what it says shows on its page.");
        return;
      }

      chosen.text().then(function (text) {
        var patch = JSON.parse(text);
        if (!patch || !Array.isArray(patch.Nodes)) throw new Error();
        show("said-description", patch.Description);
        show("said-author", patch.Author);
        show("said-tags", (patch.Tags || []).join(", "));
        said.hidden = false;
      }).catch(function () {
        tell("That file is not a Flyback patch.", true);
      });
    });

    form.addEventListener("submit", function (e) {
      e.preventDefault();
      var send = document.getElementById("send");
      send.disabled = true;
      tell("Sending…");

      fetch(api + "presets", { method: "POST", body: new FormData(form) }).then(function (r) {
        if (r.status === 429) throw new Error("That is a lot of presets at once. Try again in an hour.");
        if (r.status === 413) throw new Error("That file is too large.");
        return r.json().then(function (answer) {
          if (!r.ok) throw new Error(answer.error || "The preset was not taken.");
          location.href = "preset.html?id=" + answer.id;
        });
      }).catch(function (problem) {
        tell(problem.message || "The preset was not taken.", true);
        send.disabled = false;
      });
    });
  }

  // ---- signing in ----------------------------------------------------------

  function signIn() {
    var form = document.getElementById("sign-in");
    var inside = document.getElementById("signed-in");
    var status = document.getElementById("status");
    var reports = document.getElementById("reports");
    var letters = document.getElementById("letters");

    function show(signed) {
      form.hidden = signed;
      inside.hidden = !signed;
      reports.hidden = !signed;
      letters.hidden = !signed;
      if (signed) {
        FlybackReports.list(reports);
        FlybackLetters.list(letters);
      }
    }

    function tell(text) {
      status.textContent = text;
      status.classList.toggle("bad", !!text);
    }

    admin.then(function (state) {
      if (!state.enabled) {
        document.getElementById("admin-lede").textContent = "Admin mode is off. Set Presets__Admin__User and " +
          "Presets__Admin__Password in the container's configuration to turn it on.";
        return;
      }
      show(state.signedIn);
    });

    form.addEventListener("submit", function (e) {
      e.preventDefault();
      var enter = document.getElementById("enter");
      enter.disabled = true;
      tell("");

      fetch(api + "admin/session", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ user: form.user.value, password: form.password.value }),
      }).then(function (r) {
        if (r.status === 429) throw new Error("Too many tries. Wait a quarter of an hour.");
        if (!r.ok) throw new Error("That user and password are not the admin's.");
        form.password.value = "";
        show(true);
      }).catch(function (problem) {
        tell(problem.message);
      }).then(function () {
        enter.disabled = false;
      });
    });

    document.getElementById("sign-out").addEventListener("click", function () {
      fetch(api + "admin/session", { method: "DELETE" }).then(function () { show(false); });
    });
  }

  var which = document.body.getAttribute("data-page");
  if (which === "shelf") shelf();
  else if (which === "preset") one();
  else if (which === "submit") submit();
  else if (which === "admin") signIn();
})();
