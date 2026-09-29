# Terminology

## Correct the user's word on the spot

When the user writes a word that `docs/glossary.md` lists under **Not** for the thing they mean, say so at the top of the reply, in one line, before anything else: "*agent* → **assistant** (the glossary keeps *agent* for the one building Flyback)." Then carry on with the task using the right word.

**Why:** the user asked for it outright. The glossary is only worth keeping if the words hold in conversation too, and a wrong word said to a session ends up in its comments, test names and commit messages.

**How to apply:**

- Check the user's message against the glossary's **Not** columns: *agent* for the assistant, *node* or *port* for a module or socket, *audio* or *video* in prose, *connection* for a wire, and the rest.
- One line per word, once per session per word, and only where the meaning is clear. If the word could be right (*agent* meaning Claude Code itself, *output* meaning the Output module), say nothing.
- Code names are not wrong: `NodeDef`, `PortSpec` and `CompileForAudio` are what the source calls things, so quoting them is fine.
- Never adopt the user's word in anything written to the repo. The glossary's **Say** is what lands.
