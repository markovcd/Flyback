# A decision model that runs here: the Laya plugin, and what to ask it

Planned on 2026-09-28; the rest of it landed on 2026-10-08 and was measured against a local
laya-serve the same day, on `main` at `71fb09b2`, and again once each use could name its checkpoint. It is on TODO.md; take it off there, and
delete this file, in the commit that lands the Laya plugin.

## Where it stands

Landed ([ADR-0186](../adr/0186-a-decision-model-answers-typed-questions-behind-the-plugin-boundary.md)):
the contract in `src/Flyback.Plugins/Decide` (`IDecisionModel`, `IPreparedModel`,
`ModelFile`, the questions and answers, `SystemOneWire`), `Decisions` as the one door,
`ModelStore` downloading pinned files, `DecisionSettings` in the `decisions` section of
`settings.json`, the Decision server plugin (`src/plugins/Flyback.Plugins.SystemOne`), `flyback-cli
decide`, Settings → Decisions, and the uses: the module list by meaning (`ModuleFinder`,
`modules --find`), complaints in order (`IssueTriage`, `check --triage`, the status line)
and the assistant's messages read before sending (`TurnReading`).

Until somebody chooses, questions go to the likeliest installed model that sends nothing
anywhere. Today none is installed, so nothing is asked until the Decision server is chosen. Laya is the
model that makes the default do something.

Left out of the landed work on purpose:

- The forgotten-proposal follow-up: `TurnLoop.Unproposed` already tells the assistant once.
- The workbench's `find_modules` by meaning: a tool answer is synchronous, and an assistant
  searches well enough by words.
- Senses off for a turn read as a question: the workbench decides senses per conversation,
  not per turn.

## Measured against a local laya-serve

### Running one

On this Linux machine, Laya 0.4.0 is in `~/laya-venv` (Python 3.14, `torch` 2.14.1+cpu installed
first from `https://download.pytorch.org/whl/cpu`, then `pip install "laya[serve]"`), and both
checkpoints are in the Hugging Face cache. Serve it on loopback only; its default is 0.0.0.0:

```bash
LAYA_DEVICE=cpu LAYA_HOST=127.0.0.1 LAYA_PORT=8000 LAYA_MODELS=english,typed-decisions LAYA_PRELOAD=1 ~/laya-venv/bin/laya-serve
```

The Decision server plugin reaches it at its default endpoint, `http://localhost:8000`, with no key. A request's
`model` names the checkpoint (`english`, `multilingual`, `typed-decisions`); any other name, or
no `model` at all, which is the plugin's default, is routed by the text's language, so English text gets
`english`. The answers carry fields `SystemOneWire` does not read (`answer_confidence`, `action`,
`routing`). At startup the English checkpoint warns that its `choice:11+` temperature is invalid,
so a choice of eleven or more options is uncalibrated.

The `flyback-cli` on PATH is the installed release; the one with `decide` is a build of `main`
with `-c "All plugins"`, under `src/Flyback.Cli/bin/All plugins/net10.0/`. The `decisions` section
of `~/.config/Flyback/settings.json` holds
`{"Model":"systemone","Choices":{"systemone":{"endpoint":"http://localhost:8000"}}}`,
written by hand before `decide` could set it (its earlier copy is `settings.json.before-decisions`).
Now it is set from the command line, the module search on its own checkpoint:

```bash
flyback-cli decide --model systemone --save
flyback-cli decide --for modules --set model=typed-decisions --save
```

To try a setting without touching the real file, point `XDG_CONFIG_HOME` at a scratch folder, or
pass `--set` without `--save` for one `decide` run.

`scripts/decide-bench.py` holds the sets below: `find <cli>` scores `modules --find`, and
`route <endpoint> [checkpoint]` and `does <endpoint> [checkpoint]` score TurnReading's two
questions by its own rules.

### What was found

The sets are in `scripts/decide-bench.py`: for the search, the original fourteen phrases,
twenty-four more written with the modules' words in hand, and twenty-four held out, written
afterwards and scored apart; for the reading, thirty messages and twenty-eight proposals, eight
of them as the assistant writes them. Measured on 2026-10-08 against laya-serve 0.4.0 on this
machine's CPU, the search on typed-decisions and the reading on English.

**The module search**, as the model alone ranks it (a lab harness posting straight to the
server; the command line adds the spelled matches below):

| Option shape | First, of 62 | Top three | Held out, first | Held out, top three | A search |
|---|---|---|---|---|---|
| Type id as the label, the name as its description (what shipped first) | 23 | 27 | 4 of 24 | 5 | 6.6 s |
| A sentence of the description beside the name | 2 of 38 | 3 of 38 | — | — | 10.5 s |
| The name alone as the label | 23 | 27 | 4 | 5 | 2.4 s |
| The name as the label, the words as its description | 32 | 38 | 7 | 9 | ~4 s |
| The name and its words as the label | 31 | 45 | 7 | 14 | ~4 s |
| The same, and the ten that did best asked once more | **41** | **49** | **13** | **15** | ~5 s |

- The model keys on the label. A type id there is noise, a sentence anywhere settles it on
  the first options shown, and a few words in the label are read where the same words as a
  description are half ignored.
- Groups of ten beat five and sixteen; a third or fourth order gained nothing and four
  orders with words made a request the server refuses as too large (413).
- What a phrase spells in a name or the words is matched before any model is asked, in the
  module list and on the command line, and listed first: "a clap" finds Hiss and "portamento"
  finds Slew with no model at all, and the held-out jargon is mostly of that kind.
- The twenty-four held-out phrases are the honest number; the twenty-four "more" were
  written by the same hand as the words, within an hour, and lean on them.
- Through `flyback-cli modules --find`, spelled matches first and the model's ranking after: 43 of 62
  first and 53 in the top three; the held-out set 14 and 17 of 24; the original fourteen 10 and 13,
  from 8 and 8 at the day's start. The slowest search was 6.3 s, most under 5 s.

**The turn reading**, on English (typed-decisions reads a module question as such at only
0.5 to 0.75 and is not for this use):

| | Caught | Wrongly acted on |
|---|---|---|
| Acting on "module" alone at 0.8 (what shipped first), of 12 module questions and 18 others | 12 of 12 | 3 of 18, all of them questions about the patch |
| Acting on "module" plus "question" at 0.8, of 18 questions wanting an answer and 12 changes | 16 of 18 | 0 of 12 |

The two it misses are "why is it so quiet?" and "why does my patch show nothing?", read as not
about Flyback. The changes top out at 0.66 and the questions start at 0.91, so 0.8 sits in
the gap. The bar below 0.7 starts telling edits to answer.

**The doubt**, a yes-no on whether what the assistant says it made does what was asked:

| State | Right, of 28 | Good work doubted |
|---|---|---|
| With a line naming the patch's modules (what shipped first), at 0.5 | 17 | 8 of 14 |
| The assistant's summary alone, at 0.5 | 22 | 3 of 14 |
| The assistant's summary alone, at 0.4 (what ships) | 23 | 3 of 14 |

The patch line pulled every answer toward no, so the editor doubted most good work. What is
left wrong needs what the model does not know: that halving a tempo is slower, that a Drum on
a Euclid of four in four is a kick on every beat, that a Rotate spins. A choice or a score in
place of the yes-no did no better.

**Through Claude Code**, with the Decision server chosen and `sonnet` at high effort: "what is a
Slew for?" on the Acid preset was read as a question about a module at 0.94, Claude Code was told
to answer, and did, changing nothing; "add a touch of reverb to the pad" was read as a module
question at 0.51, under the bar, built a Reverb on the pad's channel, and its proposal was not
doubted. The reading costs one request of about 0.3 s a turn.

**The complaints' order**, on sixteen text patches each broken two or three ways, one of them
plainly why it is silent or dark (a Sample, an Image or a MIDI File with no file, a MIDI In on an
instrument that is not here, a Receive with no Send) beside harmless warnings (a wire swinging
past a socket's range, an Auto remap on plain numbers, a Clock In on an absent instrument):

| Shape | Cause first, of 16 | Every cause before every harmless complaint |
|---|---|---|
| One score question per complaint, the patch summary as the state (what shipped first) | 0 | 0 |
| One yes-no per complaint, the summary as the state | 5 | 4 |
| One choice among the complaints | 6 on English, 8 on typed-decisions | the same |
| One request per complaint, the complaint as the state, the question fixed (what ships) | **16** | **16** |

The first shape was backwards, not merely weak: "swings past the range" scored above "plays
silence" every time. With the complaint as the text the question is about, the causes score 0.3
to 0.95 and the harmless ones 0.05 to 0.2, on both checkpoints. The set tests whether the model
reads what a complaint says, which is the whole of what it can know; a complaint about a module
nowhere near the Output would score the same.

### Next

Landed: each ask names its use and the settings may lay a checkpoint over it, the module search
asks two orders and averages, `decide --set`, `--for` and `--save` set a model up, the
Decisions and Assistant pages take a key through one control, `Settings/KeyRows`, and
Settings → Decisions shows and keeps a use's settings through its **For** picker, with what each
use lays over listed under the form.

Ruled out: **a start that warms the model.** Measured twice on 2026-10-08 from a cold
laya-serve started with `LAYA_PRELOAD=1`, the first module search answered in 5.7 s and 6.1 s,
the same as the steady 6 s, and a yes-no in 0.3 s. The misses seen earlier were not the
server's warm-up; nothing in Flyback needs to ask ahead.

Left:

- **Jev, an alternative to Laya**, comes later as a model of its own. The name is kept for it
  and names nothing else.
- **A complaint off the Output's path.** The triage reads only the complaint, so a Sample with no
  file that feeds nothing still scores as why the patch is silent. Saying in the state whether the
  module reaches the Output, and a set with such cases, would tell whether the model can use it.

Measure anything new against the server with `scripts/decide-bench.py` before it lands.

## What is left: Laya, in the box

Laya (Convai Innovations, Apache-2.0) answers in one forward pass, 200–460 ms on a CPU. The
plugin ships ONNX Runtime and a tokenizer; the host downloads the model itself, once, after
a yes, from a Hugging Face repo the maintainer owns.

### Once, by the maintainer: the export

`scripts/laya/export-onnx.ps1` runs a venv with laya (0.3.20 there; 0.4.0 is what `~/laya-venv` here has, and the port follows whichever the export used), torch CPU and the English
checkpoint (on the Windows development machine under `laya-test/`, ignored) through
Laya's `scripts/export_onnx.py --quantize`: opset 18; inputs `input_ids`,
`attention_mask`, `marker_pos`, `marker_mask`, `qtype`; outputs `logits`,
`act_logits`; per-channel INT8, about 450 MB. The script also writes test fixtures (token
ids and marker positions for about 20 states and questions, and Python's answers for
them), prints SHA-256s and sizes, and says what to upload: `laya-int8.onnx`,
`tokenizer.json`, `rl_agent_config.json` and a README crediting
`convaiinnovations/laya`. The revision and the hashes go into `LayaFiles`. This is the
one Python step, on the maintainer's machine only, and the README's build section says so.

### The plugin: `src/plugins/Flyback.Plugins.Laya`

- `LayaPlugin`, and `LayaModel : IDecisionModel, IPreparedModel`: `Credential` and
  `Endpoint` null, `Priority` above the Decision server's 10, `Unavailable` only `File.Exists`, and the
  ONNX session built on the first `DecideAsync`, never at `Register`.
- `LayaFiles`: the three files with their pinned revision, sizes and SHA-256s.
- `LayaTokenizer`: `Microsoft.ML.Tokenizers` 2.0, `BpeTokenizer.Create(BpeOptions)` with
  `ByteLevel = true` and `PreTokenizer = RobertaPreTokenizer.Instance` (the GPT-2 regex
  the checkpoint's pre-tokenizer is), NFC by `string.Normalize()` first. 50,280 vocab,
  50,009 merges, specials `[CLS]`=50281 `[SEP]`=50282 `[PAD]`=50283 `[MASK]`=50284.
  Where the package disagrees with Hugging Face's on a fixture, the fallback is a
  hand-written GPT-2 BPE of about 150 lines, the ADR-0019 way.
- `Sequence`: the port of Laya's `build_sequence` and `render_options`: `[CLS] <type>
  question: <instructions> [SEP] [MASK] opt0 [MASK] opt1 … [SEP] state [SEP]`, `max_len`
  512, `head_max_len` 192, 48 tokens per option, the budget rule at `common.py:102-107`,
  left truncation for list states.
- `Calibration`: a temperature per `temperature_by_options` bucket, clamped to 0.5–5.0,
  and confidence from entropy. The English checkpoint ships an invalid temperature for a
  choice of 11 or more options, so those confidences are uncalibrated.
- `LayaSession`: the ORT `InferenceSession`, collation and softmax.
- Packages: `Microsoft.ML.OnnxRuntime` 1.30 and `Microsoft.ML.Tokenizers` 2.0, pinned in
  `Directory.Packages.props`, lock files regenerated with `dotnet restore Flyback.slnx
  --force-evaluate`. Check them first (the `nuget-packages` skill).
- Per platform: `Directory.Build.targets` gets a `Native="true"` metadata on
  `PluginProject` that passes `RuntimeIdentifier=$(RuntimeIdentifier)` to the plugin's
  two MSBuild calls, and the csproj sets `RuntimeIdentifiers` for win-x64, win-arm64,
  osx-arm64, osx-x64 and linux-x64 with `AppendRuntimeIdentifierToOutputPath=false`.
  ADR-0028 reserved this revisit for this plugin alone. `PluginLoadContext.LoadUnmanagedDll`
  already finds natives through the plugin's `.deps.json`.
- Listed as a `PluginProject` in `Flyback.Editor.Desktop.csproj`, `Flyback.Plugins.Tests`
  and `Flyback.Specs`, and in `Flyback.slnx`. Once it names `IPreparedModel` and
  `ModelFile`, both come off `ContractSurfaceTests.Promised`.

### Tests

`tests/Flyback.Plugins.Laya.Tests`: the tokenizer and `Sequence` against the export's
fixtures, `Calibration` against Python's numbers, and `LayaSession` within 0.02 of
Python's answers behind `Assert.SkipWhen(!File.Exists(model), "no Laya model on this
machine")`. `Plugins.Tests`: it loads, `Unavailable` says "not downloaded" with no model
on disk and no session built. By hand once the export is up: `flyback-cli decide
--prepare`, then `flyback-cli decide "we were billed twice" --yes-no "Is this about
money?" --json`, and the same through a `laya-serve` on the Decision server plugin, to compare.

### Docs, in the same commit

`docs/plugin-guide.md` (the plugin in the shipped list), `docs/engineering-guide.md`'s
table of what ships, `site/index.html`, the README's build note for the export script, a
`CHANGELOG.md` bullet, and an amendment to ADR-0186 for the native build.
