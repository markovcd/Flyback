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
