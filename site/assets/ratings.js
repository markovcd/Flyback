// A preset's or plugin's stars: shown on every card, given only on its own page.
window.FlybackRatings = (function () {
  var api = "api/v1/";
  var most = 5;

  function make(tag, attrs, text) {
    var node = document.createElement(tag);
    for (var key in attrs || {}) node.setAttribute(key, attrs[key]);
    if (text != null) node.textContent = text;
    return node;
  }

  function said(rating) {
    if (!rating || !rating.count) return "Not rated yet";
    return rating.average.toFixed(1) + " (" + rating.count + (rating.count === 1 ? " rating)" : " ratings)");
  }

  /** The average as stars, to read and not to click. */
  function stars(rating) {
    var box = make("span", { class: "stars", title: said(rating) });
    var whole = rating && rating.count ? Math.round(rating.average) : 0;
    var row = make("span", { class: "row", "aria-hidden": "true" });
    for (var i = 1; i <= most; i++) row.appendChild(make("span", { class: i <= whole ? "on" : "" }, "★"));
    box.appendChild(row);
    box.appendChild(make("span", { class: "said" }, said(rating)));
    return box;
  }

  /** Five stars to give, with the average and what this visitor gave before. */
  function widget(kind, id) {
    var at = api + kind + "s/" + encodeURIComponent(id) + "/rating";
    var box = make("div", { class: "rate" });
    var row = make("div", { class: "row", role: "radiogroup", "aria-label": "Your rating" });
    var status = make("p", { class: "said", role: "status" });
    var buttons = [];
    var mine = 0;

    function light(upTo) {
      buttons.forEach(function (button, i) { button.classList.toggle("on", i < upTo); });
    }

    function show(rating) {
      mine = rating.mine || 0;
      light(mine);
      buttons.forEach(function (button, i) { button.setAttribute("aria-checked", String(i + 1 === mine)); });
      status.textContent = said(rating) + (mine ? " · you gave it " + mine : "");
    }

    for (var i = 1; i <= most; i++) (function (value) {
      var button = make("button", {
        type: "button", role: "radio", "aria-checked": "false",
        "aria-label": value + (value === 1 ? " star" : " stars"), title: value + (value === 1 ? " star" : " stars"),
      }, "★");
      button.addEventListener("mouseenter", function () { light(value); });
      button.addEventListener("focus", function () { light(value); });
      button.addEventListener("click", function () {
        buttons.forEach(function (b) { b.disabled = true; });
        fetch(at, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ stars: value }),
        }).then(function (r) {
          if (r.status === 429) throw new Error("That is a lot of ratings at once. Try again in an hour.");
          if (!r.ok) throw new Error("The rating was not taken.");
          return r.json();
        }).then(show, function (problem) {
          light(mine);
          status.textContent = problem.message;
        }).then(function () {
          buttons.forEach(function (b) { b.disabled = false; });
        });
      });
      buttons.push(button);
      row.appendChild(button);
    })(i);

    row.addEventListener("mouseleave", function () { light(mine); });
    row.addEventListener("focusout", function () { light(mine); });

    box.appendChild(row);
    box.appendChild(status);

    fetch(at).then(function (r) {
      if (!r.ok) throw new Error();
      return r.json();
    }).then(show, function () { status.textContent = "The rating could not be read."; });

    return box;
  }

  return { stars: stars, widget: widget };
})();
