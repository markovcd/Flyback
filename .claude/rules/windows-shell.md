# Windows shell pitfalls

## PowerShell `Get-Content` mojibakes non-ASCII text

Never use PowerShell's `Get-Content -Raw` (without an explicit `-Encoding utf8`) for bulk find/replace on this repo's files. Its prose uses em dashes, curly quotes and other non-ASCII characters.

Windows PowerShell 5.1 reads with the system ANSI codepage (Windows-1252), not UTF-8. A UTF-8 file without a BOM, the norm for source files, has each multi-byte character (`—` is `E2 80 94`) decoded as three separate characters. Writing that string back bakes the corruption into every em dash and curly quote in the whole file, not only the lines the script touched. It once happened across 30+ files and needed a byte-level round-trip repair, which is lossy after a second pass.

For any PowerShell bulk text replace, read with `[System.IO.File]::ReadAllText($path)`, which detects UTF-8 without a BOM correctly, and never mix the two. After a bulk script touches non-ASCII prose, grep the touched files for `â€` and `�` before calling the task done. Prefer the `Edit` tool for anything more than a handful of files when correctness matters more than speed.

## Backslash escapes in Bash heredocs

In this environment a `\n`, `\\n` or `\r\n` typed inside a Bash-tool heredoc, quoted `<<'EOF'` included, reaches python or the file as a real line break. That broke C# string literals (`'\n'` split across two lines) several times. The Write tool has also turned `\a` in a C# literal into a raw BEL character.

Make any edit that contains a backslash escape with the Edit tool. In a python helper, build a backslash as `chr(92)`. In test data, write control characters as `(char)7`, not `\a`. Write long scripts with the Write tool, since a long Bash heredoc can fail to parse here. After a scripted edit, scan the changed files for control characters.
