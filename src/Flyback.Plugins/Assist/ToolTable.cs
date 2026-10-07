using System.Text.Json;
using Flyback.Core.Graph;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Every tool the workbench knows, each with what a provider is told of it and the
/// handler that runs it.
/// </summary>
internal sealed class ToolTable(
    PatchReports reports,
    WholePatchEdits whole,
    ModuleEdits edits,
    GraphEdits graph,
    PatchProposals proposals,
    PatchSenses senses,
    PatchMeasurements measurements,
    CatalogReference catalog,
    WorkbenchLimits limits)
{
    /// <summary>
    /// What a tool does when it is called. Asynchronous for every tool because two of
    /// them are, and a dispatcher that had to know which is which would be back to
    /// knowing what each tool is.
    /// </summary>
    public delegate Task<ToolOutcome> ToolBody(JsonElement arguments, CancellationToken cancel);

    /// <summary>
    /// One tool, whole: what a provider is told about it, what it does, and whether
    /// it is offered at all.
    /// </summary>
    /// <remarks>
    /// The handler sits beside the schema because the two are one decision — the
    /// schema says what the arguments are and the handler reads them (ADR-0008).
    /// <paramref name="Offered"/> is a flag rather than a fence around the entry, so
    /// a tool withheld from the model is still described in one place: dispatch
    /// knows every tool regardless, and only what a model may be offered depends on
    /// the model.
    /// </remarks>
    public sealed record Tool(PatchTool Spec, ToolBody Run, bool Offered);

    /// <summary>A tool that answers out of the graph it already has, which is most of them.</summary>
    private static Tool Does(
        string name,
        Func<JsonElement, ToolOutcome> run,
        string description,
        string schema,
        bool offered = true) =>
        new(
            new PatchTool(name, description, schema),
            (arguments, _) => Task.FromResult(run(arguments)),
            offered);

    /// <summary>A tool that has to render something before it can answer.</summary>
    private static Tool Does(
        string name,
        ToolBody run,
        string description,
        string schema,
        bool offered = true) =>
        new(new PatchTool(name, description, schema), run, offered);

    public IReadOnlyList<Tool> Build(bool vision, Listener hearing, bool lookups, bool readsPresets)
    {
        List<Tool> tools =
        [
            Does("describe_patch", _ => ToolOutcome.Fine(reports.Describe()),
                "Every module in the working patch with its handle, what each input is set to or "
                + "wired from, where each output goes, and what the compiler currently says. The "
                + "first message of a conversation already opens with it.",
                "{}"),

            Does("write_patch", whole.WritePatch,
                "Builds a whole patch at once, written in the Flyback language, replacing whatever "
                + "is on the bench. Use this to build a patch: it says in one call what placing and "
                + "wiring say in dozens, and a large patch cannot be built any other way. To change "
                + "a patch that already exists, use set_knobs and connect instead — writing one "
                + "afresh replaces every module the person placed by hand, along with where they "
                + "sat on the canvas.",
                """
                {
                  "type": "object",
                  "properties": {
                    "source": {
                      "type": "string",
                      "description": "The whole patch in the language. Nothing is adopted unless all of it reads."
                    }
                  },
                  "required": ["source"]
                }
                """),

            Does("add_module", edits.AddModule,
                "Adds one module to a patch that already exists. To build a patch, use write_patch "
                + "instead — this places one module and every wire to it is another call again. "
                + "'type_id' comes from the module list. 'handle' is optional — one is made up from "
                + "the type id if you leave it out. 'knobs' sets inputs by name as you add it.",
                """
                {
                  "properties": {
                    "type_id": { "type": "string", "description": "The module's type id, e.g. osc.sine." },
                    "handle": { "type": "string", "description": "What to call it, e.g. sine1." },
                    "knobs": {
                      "type": "array",
                      "description": "Inputs to set as it is placed.",
                      "items": {
                        "type": "object",
                        "properties": {
                          "port": { "type": "string", "description": "The input's name." },
                          "value": { "type": "number" }
                        },
                        "required": ["port", "value"]
                      }
                    }
                  },
                  "required": ["type_id"]
                }
                """),

            Does("set_knobs", edits.SetKnobs,
                "Sets one or more inputs on a module that is already placed. An input with a wire "
                + "into it keeps its knob value but ignores it until the wire is removed.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "knobs": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "port": { "type": "string" },
                          "value": { "type": "number" }
                        },
                        "required": ["port", "value"]
                      }
                    }
                  },
                  "required": ["handle", "knobs"]
                }
                """),

            Does(Vocabulary.SetSteps, edits.SetSteps,
                "Replaces the whole tune on a sequencer. Its notes are a list on the module rather "
                + "than knobs, so this is the only way to write one — send every note in order, "
                + "because this replaces what was there. 'value' is a note number on a Note "
                + "Sequencer (57 is A3) and an ordinary signal on a Sequencer. 'length' is how "
                + "many steps the note lasts and defaults to 1; 'volume' is 0 to 1, a level rather "
                + "than a switch, and defaults to 1 — a note at 0 is a rest that still holds its "
                + "value.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "notes": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "value": { "type": "number" },
                          "length": { "type": "number" },
                          "volume": { "type": "number" }
                        },
                        "required": ["value"]
                      }
                    }
                  },
                  "required": ["handle", "notes"]
                }
                """),

            Does(Vocabulary.SetScale, edits.SetScale,
                "Replaces the whole scale on a Quantiser. Its notes are a list on the module "
                + "rather than knobs, so this is the only way to set one — send every note you "
                + "want, because this replaces what was there. They are pitch classes, 0 to 11, "
                + "where 0 is C, 2 is D, 4 is E and 9 is A: naming one puts every octave of it "
                + "in the scale rather than a single note, which is what makes a scale repeat up "
                + "the keyboard. Order and repeats do not matter. C major is [0,2,4,5,7,9,11] "
                + "and a minor pentatonic on A is [0,3,5,7,10]. All twelve snaps to the nearest "
                + "semitone, which is what a Note module already does; an empty list is a wire.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "notes": {
                      "type": "array",
                      "items": { "type": "integer", "minimum": 0, "maximum": 11 }
                    }
                  },
                  "required": ["handle", "notes"]
                }
                """),

            Does(Vocabulary.SetArrangement, edits.SetArrangement,
                "Replaces every part on an Arrangement. Its parts are a grid on the module rather "
                + "than knobs, so this is the only way to write them — send every part, because "
                + "this replaces what was there. Each part is one string with a level for each "
                + "section, in order: a number, with '>' before one that glides there from the "
                + "section before's level across the whole section. Parts "
                + "shorter than the longest hold at nought to the end. Up to 8 parts of up to 32 "
                + "sections; part N comes out of the socket 'part N'.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "parts": { "type": "array", "items": { "type": "string" } }
                  },
                  "required": ["handle", "parts"]
                }
                """),

            Does(Vocabulary.SetKeyboard, whole.SetKeyboard,
                "Lays the computer keyboard out for whoever plays a MIDI In listening to it. "
                + "'piano' is the tracker layout, and the default. 'scale' puts the seven notes of "
                + "'scale' on 'tonic', a pitch class 0 to 11, side by side along the A row from the "
                + "tonic, with the Q row an octave up and the Z row an octave down — so a player can "
                + "only hit notes in the key. The scales are an Auto Chord's. One layout for the "
                + "whole patch, since there is one keyboard.",
                $$"""
                {
                  "properties": {
                    "layout": { "type": "string", "enum": ["piano", "scale"] },
                    "tonic": { "type": "integer", "minimum": 0, "maximum": 11 },
                    "scale": { "type": "string", "enum": [{{string.Join(", ", Chords.Scales.Select(scale => $"\"{scale.Id}\""))}}] }
                  },
                  "required": ["layout"]
                }
                """),

            Does(Vocabulary.SetLength, whole.SetLength,
                "Says how long the patch plays for, in 'seconds', from a tenth of a second to a day. "
                + "It is the end of the seek bar, where the patch stops or loops, and what a module "
                + "reading t.length and t.progress measures against. A patch that never says plays for "
                + "three minutes in the editor. One length for the whole patch.",
                """
                {
                  "properties": {
                    "seconds": { "type": "number", "minimum": 0.1, "maximum": 86400 }
                  },
                  "required": ["seconds"]
                }
                """),

            Does(Vocabulary.SetSample, edits.SetSample,
                "Points a Sample module at a sound file, a MIDI File module at a .mid file, or a Path "
                + "module at an .svg, .obj or .png drawing. The path is neither a knob nor a wire, "
                + "so this is the only way to set one — and it is the one thing in a patch that "
                + "refers to something outside it, so the file has to exist where you say it "
                + "does. A WAV (mono or stereo, 8 to 32 bit or float) or an MP3, no other format. The "
                + "answer says whether it could be read, so a path that is wrong is answered "
                + "now rather than by silence later. Ask the person for a path rather than "
                + "guessing at one — nothing here can list what is on their machine. A MIDI File's voice "
                + "and channel are set with set_extra.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "path": { "type": "string", "description": "Where the WAV, MP3, MIDI, SVG, OBJ or PNG file is, absolute or beside the patch." }
                  },
                  "required": ["handle", "path"]
                }
                """),

            Does(Vocabulary.SetPicture, edits.SetPicture,
                "Points an Image module at a picture file. The path is neither a knob nor a wire, "
                + "so this is the only way to set one, and like a sample it refers to something "
                + "outside the patch — the file has to exist where you say it does. A PNG: 8 or 16 "
                + "bit, not interlaced. The answer says whether it could be read, so a path that is "
                + "wrong is answered now rather than by a black picture later. Ask the person for a "
                + "path rather than guessing at one — nothing here can list what is on their "
                + "machine.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "path": { "type": "string", "description": "Where the PNG is, absolute or beside the patch." }
                  },
                  "required": ["handle", "path"]
                }
                """),

            Does(Vocabulary.SetExtra, edits.SetExtra,
                "Sets one named value on a module that carries something a plugin defined — the "
                + "rows a module listing writes under a name of their own, like 'notes' or "
                + "'chord', rather than as 'in N'. Take the extra's name and the field's from "
                + "that listing; describe_module says what a given module carries and what each "
                + "field may hold. A number is clamped to the field's range, a switch takes "
                + "true or false, a choice takes the id of one of the things it offers — "
                + "describe_module lists them, and a refusal names them too — and text takes a "
                + "string, with \\n between lines where it holds several. The built-in "
                + "notes, scale, parts, file and picture are not set this way: they have set_steps, "
                + "set_scale, set_arrangement, set_sample and set_picture.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "extra": { "type": "string", "description": "Which extra, as the listing names it." },
                    "field": { "type": "string", "description": "Which of its values." },
                    "value": { "description": "A number, a boolean, a choice's id as a string, or text — matching the field." }
                  },
                  "required": ["handle", "extra", "field", "value"]
                }
                """),

            Does("connect", graph.Connect,
                "Wires an output to an input. 'from_port' may be left out when the source has only "
                + "one output, which most modules do. An input takes one wire, so this replaces "
                + "whatever was there and tells you what it replaced.",
                """
                {
                  "properties": {
                    "from": { "type": "string", "description": "Handle of the module the signal leaves." },
                    "from_port": { "type": "string", "description": "Output name. Optional when there is only one." },
                    "to": { "type": "string", "description": "Handle of the module the signal arrives at." },
                    "to_port": { "type": "string", "description": "Input name." }
                  },
                  "required": ["from", "to", "to_port"]
                }
                """),

            Does("disconnect", graph.Disconnect,
                "Removes the wire feeding an input, which puts that input back on its own knob.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "port": { "type": "string", "description": "The input's name." }
                  },
                  "required": ["handle", "port"]
                }
                """),

            Does("switch_module", graph.SwitchModule,
                "Switches a module off, or back on with 'off' false. A module that is off is a "
                + "wire: what is patched into it comes straight out of it, and where nothing is "
                + "patched in nothing comes out and whatever it fed is back on its own knob. Use "
                + "it to hear a patch without one part of it, which deleting the module would "
                + "lose the settings of.",
                """
                {
                  "properties": {
                    "handle": { "type": "string" },
                    "off": { "type": "boolean", "description": "False switches it back on. Defaults to true." }
                  },
                  "required": ["handle"]
                }
                """),

            Does("remove_module", graph.RemoveModule,
                "Deletes a module and every wire attached to it.",
                """
                { "properties": { "handle": { "type": "string" } }, "required": ["handle"] }
                """),

            Does("reset", _ => whole.Reset(),
                "Throws away every edit and goes back to the patch as it was when this started.",
                "{}"),

            Does("propose", proposals.ProposeAsync,
                "Offers the patch to the person, with one line saying what it does. This ends your "
                + "turn. Nothing you have built reaches their editor until you call this. The patch "
                + "must compile cleanly first, something must reach the Output — its 'color', "
                + "its 'left', or both — and a sound that is wired must not be silent.",
                $$"""
                {
                  "properties": {
                    "summary": { "type": "string", "description": "One line: what this patch does." },
                    "starts_silent": { "type": "boolean", "description": "True only when the sound is meant to be silent for its first {{Number(limits.LatestTime)}}s, an intro that comes in later." }
                  },
                  "required": ["summary"]
                }
                """),

            Does("render", senses.RenderAsync,
                "Draws the patch and shows you the result: several frames side by side, so you can "
                + "see movement as well as color. Use it once the shape is right, and again after "
                + "adjusting what you saw.",
                $$"""
                {
                  "properties": {
                    "from": {
                      "type": "number",
                      "description": "Where on the patch's timeline the frames start, up to {{Number(limits.LatestStart)}}. Defaults to 0. Drawing begins {{Number(limits.WarmUpLead)}}s before it, so anything that remembers earlier frames has only that much history."
                    },
                    "times": {
                      "type": "array",
                      "description": "Seconds after 'from' to capture, at most {{limits.MaxFrames}} of them, up to {{Number(limits.LatestTime)}}. Defaults to 0.5, 1.5 and 3.5.",
                      "items": { "type": "number" }
                    },
                    "note": { "type": "string", "description": "What you are looking for, for your own record." }
                  }
                }
                """,
                offered: vision),

            Does("listen", senses.ListenAsync,
                "Renders the patch's sound, measures its level, loudness and spectrum, and "
                + (hearing is Listener.Itself
                    ? "plays it to you — the clip arrives after this reply, the way a rendered "
                      + "frame does. "
                    : "has a model that can hear describe it to you. ")
                + "Use it once the sound is wired, and again after adjusting what you found. It is "
                + "the only way to find out whether a patch built for the speakers is anything at "
                + "all, as opposed to merely legal.",
                $$"""
                {
                  "properties": {
                    "seconds": {
                      "type": "number",
                      "description": "How much to render, from 0.25 to {{Number(limits.LongestListen)}}. Defaults to 2."
                    },
                    "from": {
                      "type": "number",
                      "description": "Where on the timeline to start, up to {{Number(limits.LatestStart)}}. Defaults to 0. Sound begins {{Number(limits.ListenLead)}}s before it, so delays arrive with a tail but anything longer than that is missing."
                    },
                    "note": { "type": "string", "description": "What you are listening for, for your own record. {{Kept(hearing)}}" }
                  }
                }
                """,
                offered: hearing is not Listener.None),

            Does("measure", measurements.MeasureAsync,
                "Runs the patch for a few seconds with nothing played in and says what each output "
                + "carries, as numbers: its value when it holds still, otherwise its range, mean and "
                + "how fast it moves (hertz, steps a second, or steepest slope), and whether it "
                + "varies across the picture. Every output of the modules named, wired to anything "
                + "or not, or of every module when none are named. The sound and the picture are "
                + "measured apart, since memory and coordinates make them differ. Reach for it to "
                + "check what a knob or an LFO really gives before wiring it, or why something is "
                + "flat; it costs no picture and no clip.",
                $$"""
                {
                  "properties": {
                    "handles": { "type": "array", "items": { "type": "string" }, "description": "Modules to measure. Left out, every module." },
                    "seconds": { "type": "number", "description": "How long to run, from 0.25 to {{Number(PatchMeasurements.LongestMeasure)}}. Defaults to 2. A repeat needs a few cycles in the window to be counted." },
                    "from": { "type": "number", "description": "Where on the patch's timeline the window starts, up to {{Number(limits.LatestStart)}}. Defaults to 0. Anything that remembers earlier moments starts empty there." }
                  }
                }
                """),

            Does("describe_module", catalog.DescribeModule,
                "Everything about one module: what it is for, and each port's default, range and "
                + "what it is for.",
                """
                { "properties": { "type_id": { "type": "string" } }, "required": ["type_id"] }
                """),

            Does("find_modules", catalog.FindModules,
                "Searches the module list by type id, name, category or description.",
                """
                { "properties": { "query": { "type": "string" } }, "required": ["query"] }
                """,
                offered: lookups),

            Does("describe_preset", catalog.DescribePreset,
                "Reads one of the presets in the list at the end of the briefing, written in the "
                + "Flyback language, to see how it is built. It does not touch the patch on the bench.",
                """
                { "properties": { "name": { "type": "string" } }, "required": ["name"] }
                """,
                offered: readsPresets),
        ];

        return tools;
    }

    /// <summary>
    /// What becomes of <c>listen</c>'s <c>note</c>, which depends on whether there
    /// is anybody else to keep it from.
    /// </summary>
    /// <remarks>
    /// Withholding it is the second-hand arrangement's one safeguard — a listener
    /// told what to listen for will find it, and ADR-0047 records the clip where it
    /// did. There is nobody to withhold it from when the model plays itself the
    /// clip.
    /// </remarks>
    private static string Kept(Listener hearing) => hearing is Listener.Itself
        ? "Nobody else reads it."
        : "It is deliberately not passed on: whoever listens is told nothing about the patch, "
          + "so that what comes back could disagree with you.";
}
