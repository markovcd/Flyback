// Colors the text language the way the app's text view does. The rules are
// Flyback.xshd's, in the same order, so a snippet here reads like the F2 view.
// The website's pages and embed.js on other sites both color with it.
window.flybackHighlight = (function () {
  // Statement words are told from names of the same spelling by what follows them.
  var keywords = new RegExp([
    "\\b(?:let|def|group|for|off)\\b",
    "\\bin\\b(?=[ \\t]+[^\\s:,)=.])",
    "\\b(?:description|author|tags)\\b(?=[ \\t]*\")",
    "\\brequires\\b(?=[ \\t]+[\"\\w])",
    "\\bpanel\\b(?=[ \\t]+\\w+[ \\t]*=)",
    "\\b(?:held|switch|button|latching)\\b(?=[ \\t]*(?:,|\/\/|\\n|$))",
    "\\bkeyboard[ \\t]+(?:piano|scale)\\b",
    "\\blength\\b(?=[ \\t]+\\d)",
  ].join("|"), "y");

  var code = [
    ["t-com", /\/\/[^\n]*/y],
    ["t-str", /"[^"\n]*"?/y],
    ["t-key", keywords],
    ["t-sink", /\bout\s*\.\s*\w+/y],
    ["t-dom", /\b(?:x|y|t|radius|angle|aspect)\b(?!\s*[:(])/y],
    ["t-sock", /\b[A-Za-z_]\w*(?=\s*:)/y],
    ["t-num", /\b[A-G][#b]?-?\d+\b/y],
    ["t-mod", /\b[A-Za-z_][\w.]*(?=\s*\()/y],
    ["t-pipe", /\|>|<-|\.\.\.?|\b_\b/y],
    ["t-num", /\b\d+(?:\.\d+)?(?:ms|us|s)?\b/y],
  ];

  // Inside brackets a note may leave out its octave, and the step and
  // arrangement notation has operators of its own.
  var block = [
    ["t-num", /\b[A-G][#b]?(?:-?\d+)?(?![\w#])/y],
    ["t-com", /~/y],
    ["t-pipe", /\|>|<-|\.\.\.?|\b_\b|[|<>!@%]/y],
  ].concat(code);

  function escape(text) {
    return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  }

  function highlight(source) {
    var html = "";
    var plain = "";
    var depth = 0;
    var i = 0;

    outer: while (i < source.length) {
      var c = source[i];
      if (c === "[" || (c === "]" && depth > 0)) {
        depth += c === "[" ? 1 : -1;
        plain += c;
        i++;
        continue;
      }
      var rules = depth > 0 ? block : code;
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

  return highlight;
})();
