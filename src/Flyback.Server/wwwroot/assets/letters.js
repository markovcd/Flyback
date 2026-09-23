// The admin's list of letters people wrote from inside Flyback.
window.FlybackLetters = (function () {
  var api = "api/v1/";

  var moods = [
    ["good", "Something works well"],
    ["bad", "Something is wrong"],
    ["idea", "Something I would like"],
    ["other", "Something else"],
  ];

  function said(mood) {
    for (var i = 0; i < moods.length; i++) if (moods[i][0] === mood) return moods[i][1];
    return mood;
  }

  function make(tag, attrs, text) {
    var node = document.createElement(tag);
    for (var key in attrs || {}) node.setAttribute(key, attrs[key]);
    if (text != null) node.textContent = text;
    return node;
  }

  /** Every letter, newest first, each with a Dismiss. */
  function list(into) {
    fetch(api + "letters").then(function (r) {
      if (!r.ok) throw new Error();
      return r.json();
    }).then(function (letters) {
      into.replaceChildren(make("h2", null, letters.length ? "Letters" : "No letters"));

      letters.forEach(function (letter) {
        var card = make("article", { class: "card report-card" });
        var head = make("header");

        head.appendChild(make("span", { class: "eyebrow" }, said(letter.mood)));
        if (letter.contact) head.appendChild(make("span", null, letter.contact));
        else head.appendChild(make("span", { class: "quiet" }, "No address"));
        card.appendChild(head);

        var body = make("div", { class: "body" });
        body.appendChild(make("p", null, letter.message));

        var about = [letter.version && "Version " + letter.version, letter.platform, letter.plugins]
          .filter(Boolean).join(" · ");
        if (about) body.appendChild(make("p", { class: "details" }, about));

        var foot = make("div", { class: "foot" });
        foot.appendChild(make("span", null, new Date(letter.submitted).toLocaleString()));
        var dismiss = make("button", { type: "button", class: "button small" }, "Dismiss");
        dismiss.addEventListener("click", function () {
          dismiss.disabled = true;
          fetch(api + "letters/" + letter.id, { method: "DELETE" }).then(function (r) {
            if (!r.ok) throw new Error();
            list(into);
          }).catch(function () { dismiss.disabled = false; });
        });
        foot.appendChild(dismiss);
        body.appendChild(foot);

        card.appendChild(body);
        into.appendChild(card);
      });
    }).catch(function () {
      into.replaceChildren(make("p", { class: "status bad" }, "The letters could not be read."));
    });
  }

  return { list: list };
})();
