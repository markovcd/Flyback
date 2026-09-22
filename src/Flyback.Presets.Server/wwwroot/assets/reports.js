// Reporting a preset or plugin to the admin, and the admin's list of reports.
window.FlybackReports = (function () {
  var api = "api/v1/";

  var reasons = [
    ["broken", "It does not open or does not work"],
    ["harmful", "It is harmful, or does something it does not say"],
    ["offensive", "It is offensive"],
    ["stolen", "It is somebody else's work"],
    ["other", "Something else"],
  ];

  function said(reason) {
    for (var i = 0; i < reasons.length; i++) if (reasons[i][0] === reason) return reasons[i][1];
    return reason;
  }

  function make(tag, attrs, text) {
    var node = document.createElement(tag);
    for (var key in attrs || {}) node.setAttribute(key, attrs[key]);
    if (text != null) node.textContent = text;
    return node;
  }

  /** A Report button that opens the form beneath it, for the preset or plugin called name. */
  function form(kind, id, name) {
    var box = make("div", { class: "report" });
    var open = make("button", { type: "button", class: "button small quiet-button" }, "Report");
    var sheet = make("form", { class: "report-form", hidden: "" });

    sheet.appendChild(make("p", { class: "quiet" },
      "Tell the admin what is wrong with “" + name + "”. It stays listed until they have looked."));

    var list = make("fieldset");
    list.appendChild(make("legend", null, "What is wrong"));
    reasons.forEach(function (reason, i) {
      var label = make("label");
      var radio = make("input", { type: "radio", name: "reason", value: reason[0] });
      if (i === 0) radio.required = true;
      label.appendChild(radio);
      label.appendChild(document.createTextNode(reason[1]));
      list.appendChild(label);
    });
    sheet.appendChild(list);

    var details = make("textarea", { name: "details", rows: "3", maxlength: "1000", placeholder: "What happened (optional)" });
    sheet.appendChild(details);

    var row = make("div", { class: "actions" });
    var send = make("button", { type: "submit", class: "button small primary" }, "Send report");
    var cancel = make("button", { type: "button", class: "button small" }, "Cancel");
    row.appendChild(send);
    row.appendChild(cancel);
    sheet.appendChild(row);

    var status = make("p", { class: "status", role: "status" });
    sheet.appendChild(status);

    function tell(text, bad) {
      status.textContent = text;
      status.classList.toggle("bad", !!bad);
    }

    open.addEventListener("click", function () {
      sheet.hidden = false;
      open.hidden = true;
    });
    cancel.addEventListener("click", function () {
      sheet.hidden = true;
      open.hidden = false;
      tell("");
    });

    sheet.addEventListener("submit", function (e) {
      e.preventDefault();
      send.disabled = true;
      tell("Sending…");

      fetch(api + kind + "s/" + encodeURIComponent(id) + "/reports", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason: sheet.reason.value, details: details.value }),
      }).then(function (r) {
        if (r.status === 429) throw new Error("That is a lot of reports at once. Try again in an hour.");
        if (!r.ok) throw new Error("The report was not taken.");
        box.replaceChildren(make("p", { class: "status" }, "Reported. Thank you: the admin will look at it."));
      }).catch(function (problem) {
        tell(problem.message, true);
        send.disabled = false;
      });
    });

    box.appendChild(open);
    box.appendChild(sheet);
    return box;
  }

  /** The admin's list of every report, newest first, each with a Dismiss. */
  function list(into) {
    fetch(api + "reports").then(function (r) {
      if (!r.ok) throw new Error();
      return r.json();
    }).then(function (reports) {
      into.replaceChildren(make("h2", null, reports.length ? "Reports" : "No reports"));

      reports.forEach(function (report) {
        var page = report.kind === "plugin" ? "plugin.html" : "preset.html";
        var card = make("article", { class: "card report-card" });
        var head = make("header");

        head.appendChild(make("span", { class: "eyebrow" }, report.kind));
        if (report.name) head.appendChild(make("a", { href: page + "?id=" + encodeURIComponent(report.subject) }, report.name));
        else head.appendChild(make("span", { class: "quiet" }, "Deleted"));
        card.appendChild(head);

        var body = make("div", { class: "body" });
        body.appendChild(make("p", null, said(report.reason)));
        if (report.details) body.appendChild(make("p", { class: "details" }, report.details));

        var foot = make("div", { class: "foot" });
        foot.appendChild(make("span", null, new Date(report.submitted).toLocaleString()));
        var dismiss = make("button", { type: "button", class: "button small" }, "Dismiss");
        dismiss.addEventListener("click", function () {
          dismiss.disabled = true;
          fetch(api + "reports/" + report.id, { method: "DELETE" }).then(function (r) {
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
      into.replaceChildren(make("p", { class: "status bad" }, "The reports could not be read."));
    });
  }

  return { form: form, list: list };
})();
