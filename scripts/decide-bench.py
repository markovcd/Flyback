#!/usr/bin/env python3
# Measures the decision model's uses that have a right answer, against a fixed set each:
#
#   decide-bench.py find  FLYBACK_CLI           the module search, through `modules --find`
#   decide-bench.py route ENDPOINT [CHECKPOINT] the turn reading's intent question
#   decide-bench.py does  ENDPOINT [CHECKPOINT] the turn reading's doubt question
#   decide-bench.py triage FLYBACK_CLI          the complaints' order, through `check --triage`
#
# `find` asks whatever model the settings choose; run it under XDG_CONFIG_HOME pointing at a
# folder whose Flyback/settings.json names the model, to leave the real settings alone. Its
# phrases come in three sets: the original fourteen, twenty-four more written with the modules'
# words in hand, and twenty-four written afterwards, scored apart. `route` and `does` post
# TurnReading's questions straight to ENDPOINT (e.g. http://localhost:8000), naming CHECKPOINT
# (english, typed-decisions) for a laya-serve, and score them by TurnReading's own rules.
# `triage` writes sixteen text patches, each broken two or three ways with one way plainly why
# it is silent or dark, and checks that the first complaint is about that one. Standard library only.

import json
import os
import subprocess
import sys
import tempfile
import time
import urllib.request

ORIGINAL = {
    "repeat the sound like an echo": ["flyback.effects.echo", "audio.delay"],
    "random hiss": ["audio.noise", "flyback.voice.hiss"],
    "a drum hit": ["flyback.voice.drum", "flyback.easy.drummer"],
    "make a sound fade in and out": ["flyback.voice.fade", "env.adsr"],
    "something that wobbles the pitch slowly": ["flyback.voice.wander", "osc.sine"],
    "a mirror maze of shards": ["space.kaleidoscope", "space.mirror"],
    "make it sound like a big room": ["audio.reverb"],
    "louder without clipping": ["flyback.mastering.limiter", "flyback.mastering.compressor", "flyback.mastering.maximizer"],
    "smear the picture over time": ["feedback.blur", "feedback.trails"],
    "an endlessly detailed fractal": ["flyback.fractals.mandelbrot", "flyback.fractals.julia", "flyback.picture.fractal"],
    "a ringing bell": ["flyback.voice.bell"],
    "darken the edges of the picture": ["color.vignette"],
    "play notes in a repeating pattern": ["seq.notes", "flyback.voice.euclid", "seq.values"],
    "distort the sound": ["audio.drive", "flyback.voice.crush", "flyback.voice.fold"],
}
MORE = {
    "a kick drum": ["flyback.voice.drum", "flyback.easy.drummer"],
    "hi-hat or snare": ["flyback.voice.hiss", "flyback.easy.drummer"],
    "an electric piano sound": ["flyback.voice.fm"],
    "glide between notes": ["audio.slew"],
    "snap a sweep to the notes of a scale": ["audio.quantiser", "audio.tune"],
    "a checkerboard": ["pattern.checker"],
    "zoom the picture in": ["space.scale", "space.transform"],
    "spin the whole picture": ["space.rotate", "space.transform"],
    "repeat the picture in a grid": ["space.tile"],
    "a cloudy texture": ["pattern.clouds", "flyback.picture.fractal"],
    "crossfade between two colors": ["color.mix"],
    "write words on the screen": ["flyback.picture.text"],
    "a round shape": ["flyback.picture.circle"],
    "a five-pointed star": ["flyback.picture.star"],
    "make the stereo wider": ["flyback.mastering.width"],
    "squash the loud parts down": ["flyback.mastering.compressor", "flyback.mastering.limiter", "flyback.mastering.maximizer"],
    "a value that drifts randomly": ["flyback.voice.wander", "pattern.clouds"],
    "play a midi file": ["midi.file"],
    "hear the microphone": ["audio.in"],
    "lo-fi crunchy bit reduction": ["flyback.voice.crush"],
    "hits spread evenly over the bar": ["flyback.voice.euclid"],
    "fewer colors, flat bands": ["flyback.picture.posterise"],
    "a plucked string": ["osc.string"],
    "thicken a voice into several": ["flyback.effects.chorus", "flyback.voice.osc"],
}
HELDOUT = {
    "an echo that keeps time with the beat": ["flyback.effects.echo"],
    "the sound of a church": ["audio.reverb"],
    "fuzz guitar": ["audio.drive", "flyback.voice.fold"],
    "a clap": ["flyback.voice.hiss"],
    "a tom drum": ["flyback.voice.drum"],
    "a brass stab": ["flyback.voice.fm"],
    "portamento": ["audio.slew"],
    "keep the melody in key": ["audio.quantiser", "audio.tune"],
    "stripes": ["osc.saw", "osc.square"],
    "concentric circles": ["pattern.rings"],
    "snowflake symmetry": ["space.kaleidoscope", "space.mirror"],
    "bend the picture out of shape": ["space.warp"],
    "stone or cracked earth texture": ["flyback.picture.cells"],
    "a rectangle": ["flyback.picture.box"],
    "an outline around a shape": ["flyback.picture.fill"],
    "pick colors from a gradient": ["flyback.picture.palette"],
    "lower the brightness": ["color.gain", "flyback.picture.grade"],
    "ghosting of past frames": ["feedback.trails", "feedback"],
    "spin a cube": ["flyback.drawings.rotate3d", "flyback.drawings.path"],
    "a sidechain pump": ["audio.duck"],
    "equalize the highs and lows": ["flyback.mastering.eq"],
    "a wind-up riser": ["flyback.voice.hiss"],
    "an arpeggio": ["seq.notes", "audio.chord"],
    "mute the sound while a key is loud": ["audio.duck"],
}
FIND = {"original": ORIGINAL, "more": MORE, "heldout": HELDOUT}

# True for a question about a module, "answer" for any other question wanting an answer rather
# than a change, False for a change or something else. TurnReading acts on the first two.
ROUTE = {
    "what does the ADSR do?": True,
    "how do I use the Filter?": True,
    "what is a Slew for?": True,
    "explain the Kaleidoscope module": True,
    "what's the difference between Delay and Echo?": True,
    "what does the resonance knob on a filter do": True,
    "how does Feedback work in flyback": True,
    "what is the Clouds module good for?": True,
    "which module makes a kick drum?": True,
    "tell me about the Euclid module": True,
    "what are the sockets of a Vignette": True,
    "does the Reverb work on the picture too?": True,
    "why is it so quiet?": "answer",
    "why does my patch show nothing?": "answer",
    "which of my filters is making it muddy?": "answer",
    "what does my patch sound like?": "answer",
    "is my ADSR wired right?": "answer",
    "what is this patch doing?": "answer",
    "make the bass slower": False,
    "add some reverb": False,
    "start over with a drum loop": False,
    "what is the capital of France?": False,
    "load the Plasma preset": False,
    "turn the saw down and add a filter sweep": False,
    "make it more purple": False,
    "replace the sine with a saw": False,
    "write me a poem about synths": False,
    "double the tempo": False,
    "hi": False,
    "undo that": False,
}

# TurnReading's intents, its bar, and the patch the state describes.
INTENTS = {
    "edit": "Change the patch that is open",
    "new": "Build a new patch from nothing",
    "question": "A question about the patch that is open, wanting an answer rather than a change",
    "module": "A question about what a module does or how it is used",
    "preset": "Asks for one of the presets to be loaded or shown",
    "other": "Not about Flyback, patches, sound or pictures at all",
}
SURE = 0.8
PATCH = "A patch of 12 modules and 15 wires: Output, Sine, Saw, Filter, ADSR, Mixer, Note Sequencer, Tempo, Reverb, Kaleidoscope, Clouds, HSV."

# What was asked, what the assistant said it made, and whether that does it. The last eight are
# written as the assistant writes them.
DOES = [
    ("make the bass slower", "Halved the Note Sequencer's tempo so the bass line plays at half speed.", True),
    ("make the bass slower", "Added a Reverb after the Mixer.", False),
    ("add some reverb", "Patched a Reverb between the Mixer and the Output.", True),
    ("add some reverb", "Added a Chorus on the pad.", False),
    ("make it more purple", "Shifted the HSV hue toward 0.8 so the picture reads purple.", True),
    ("make it more purple", "Raised the Mixer's level on the saw.", False),
    ("replace the sine with a saw", "Swapped the Sine for a Saw, keeping its frequency wire.", True),
    ("replace the sine with a saw", "Added a second Sine an octave up.", False),
    ("double the tempo", "Set the Tempo from 120 to 240 BPM.", True),
    ("double the tempo", "Set the Tempo from 120 to 60 BPM.", False),
    ("make it louder", "Raised the Output volume from 0.5 to 0.8.", True),
    ("make it louder", "Lowered the Output volume to 0.3 to avoid clipping.", False),
    ("add a kick drum on every beat", "Added a Drum triggered by a Euclid of 4 hits in 4 steps, mixed into the Output.", True),
    ("add a kick drum on every beat", "Added a Hiss on every other step for a hi-hat.", False),
    ("spin the picture slowly", "Wired Time through a Rotate before the Kaleidoscope.", True),
    ("spin the picture slowly", "Zoomed the picture in with a Scale.", False),
    ("mute the saw", "Set the Mixer's saw level to 0.", True),
    ("mute the saw", "Set the Mixer's sine level to 0.", False),
    ("give the pad a slow attack", "Set the pad's ADSR attack to 2 seconds.", True),
    ("give the pad a slow attack", "Set the pad's ADSR release to 2 seconds.", False),
    ("add a touch of reverb to the pad", "Added a small reverb (size 0.6, decay 0.55, mix 0.2) on the pad's Desk channel, after the chorus; the shared room send is unchanged.", True),
    ("add a touch of reverb to the pad", "Added a Chorus (rate 0.3, depth 0.4) on the pad's Desk channel before the Desk; the shared room send is unchanged.", False),
    ("make the kick hit harder", "Raised the Drum's level from 0.6 to 0.9 and shortened its decay from 0.4 to 0.25 s so each hit is louder and punchier; added a Drive at 0.3 on the kick's channel.", True),
    ("make the kick hit harder", "Lowered the Drum's level from 0.6 to 0.4 and lengthened its decay to 0.8 s so it sits further back in the mix.", False),
    ("slow the whole thing down", "Set the Tempo from 128 to 96 BPM; the Note Sequencer, the Euclid and the Echo all follow it, so the melody, the drums and the echo taps slow together.", True),
    ("slow the whole thing down", "Set the Tempo from 128 to 160 BPM; the Note Sequencer, the Euclid and the Echo all follow it.", False),
    ("make the picture darker", "Lowered the Gain's brightness from 1.0 to 0.6 and added a Vignette (from 0.5, to 1.2) so the corners fall to black.", True),
    ("make the picture darker", "Raised the Gain's brightness from 1.0 to 1.4 and its contrast to 1.2 so the picture is brighter and punchier.", False),
]
DOUBTED = 0.4

# The lines the triage's patches are made of: a sound and a picture that work, three harmless
# warnings, and six complaints that each mean silence or black.
PICTURE = "hsv(hue: sine(freq: 0.1), saturation: 0.8, value: 0.9) |> out.color\n"
SOUND = "sine(freq: 220) |> out.left\n"
# Harmless complaints, each a line that adds a warning and changes nothing the ear or eye would miss.
OVERFLOW = "sine(freq: saw(freq: 0.2)) * 0 |> out.right\n"      # a Saw swinging past Sine's 'freq'
REMAP = "let lfo = sine(freq: 0.2) * 3 |> autoremap(in_low: 0, in_high: 1, out_low: 100, out_high: 1000)\nsine(freq: lfo) * 0 |> out.right\n"
BOTH = "let o = sine(freq: saw(freq: 0.2))\nlet lfo = sine(freq: 0.2) * 3 |> autoremap(in_low: 0, in_high: 1, out_low: 100, out_high: 1000)\n(sine(freq: lfo) + o) * 0 |> out.right\n"
CLOCK = "let c = midi.clock(device: \"Syntakt\")\nhsv(hue: c.beats * 0.1, saturation: 0.8, value: 0.9) |> out.color\n"
SAMPLE = "let s = sample()\ns |> out.left\n"
MIDIFILE = "let m = midi.file()\nsine(freq: m.pitch) * m.gate |> out.left\n"
MIDIIN = "let m = midi.in(device: \"Launchkey 49\")\nsine(freq: m.pitch) * m.gate |> out.left\n"
IMAGE = "picture() |> out.color\n"
RECEIVE = "receive(bus: 2) |> out.left\n"

# (name, text, the modules any of which is why; the first complaint should be about one of them)
TRIAGE = [
    ("sample-overflow", SAMPLE + PICTURE + OVERFLOW, ["Sample"]),
    ("sample-remap", SAMPLE + PICTURE + REMAP, ["Sample"]),
    ("image-remap", IMAGE + SOUND + REMAP, ["Image"]),
    ("image-overflow", IMAGE + SOUND + OVERFLOW, ["Image"]),
    ("midifile-overflow", MIDIFILE + PICTURE + OVERFLOW, ["MIDI File"]),
    ("midifile-remap", MIDIFILE + PICTURE + REMAP, ["MIDI File"]),
    ("midiin-remap", MIDIIN + PICTURE + REMAP, ["MIDI In"]),
    ("midiin-overflow", MIDIIN + PICTURE + OVERFLOW, ["MIDI In"]),
    ("sample-clock", SAMPLE + CLOCK, ["Sample"]),
    ("image-overflow-remap", IMAGE + SOUND + BOTH, ["Image"]),
    ("midiin-clock", MIDIIN + CLOCK, ["MIDI In"]),
    ("sample-image-overflow", SAMPLE + IMAGE + OVERFLOW, ["Sample", "Image"]),
    ("receive-overflow", RECEIVE + PICTURE + OVERFLOW, ["Receive", "Output"]),
    ("midifile-overflow-remap", MIDIFILE + PICTURE + BOTH, ["MIDI File"]),
    ("image-midiin-overflow", IMAGE + MIDIIN + OVERFLOW, ["Image", "MIDI In"]),
    ("sample-clock-remap", SAMPLE + CLOCK + REMAP, ["Sample"]),
]


def ask(endpoint, checkpoint, state, questions):
    body = {"state": state, "questions": questions}
    if checkpoint:
        body["model"] = checkpoint
    request = urllib.request.Request(
        endpoint.rstrip("/") + "/v1/systemone",
        data=json.dumps(body).encode(),
        headers={"content-type": "application/json"})
    try:
        return json.load(urllib.request.urlopen(request, timeout=120))["answers"]
    except OSError as nothing:
        sys.exit(f"{endpoint} did not answer ({nothing}). Is laya-serve running?")


def find(cli):
    totals = {}
    slowest = 0.0
    for which, phrases in FIND.items():
        top1 = top3 = 0
        for phrase, wanted in phrases.items():
            start = time.time()
            run = subprocess.run([cli, "modules", "--find", phrase, "--json"], capture_output=True, text=True)
            slowest = max(slowest, time.time() - start)
            try:
                found = [f["typeId"] for f in json.loads(run.stdout)]
            except json.JSONDecodeError:
                found = []
                print(f"  ! {phrase}: {run.stderr.strip() or 'no answer'}")
            hit1 = bool(found) and found[0] in wanted
            hit3 = any(f in wanted for f in found[:3])
            top1 += hit1
            top3 += hit3
            print(f"{'✓' if hit1 else '~' if hit3 else '✗'} {phrase:42} {found[:3]}")
        totals[which] = (top1, top3, len(phrases))
        print(f"{which}: first {top1}/{len(phrases)}, top-3 {top3}/{len(phrases)}")
    n = sum(t[2] for t in totals.values())
    print(f"all: first {sum(t[0] for t in totals.values())}/{n}, top-3 {sum(t[1] for t in totals.values())}/{n}, slowest {slowest:.1f}s")


def route(endpoint, checkpoint):
    caught = wrong = answers = others = 0
    for message, wanted in ROUTE.items():
        state = f"The patch: {PATCH}\nThe message: {message}"
        answer = ask(endpoint, checkpoint, state, {"intent": {"type": "choice", "instructions": "What does the message ask for?", "criteria": INTENTS}})["intent"]
        p = answer["probabilities"]
        asks = p.get("module", 0) + p.get("question", 0)
        acts = asks >= SURE
        answers += bool(wanted)
        others += not wanted
        caught += acts and bool(wanted)
        wrong += acts and not wanted
        print(f"{'✓' if acts == bool(wanted) else '✗'} {message:48} {answer['choice']} {p[answer['choice']]:.2f}, wants an answer {asks:.2f}")
    print(f"questions told to answer: {caught}/{answers}; changes and the rest wrongly told: {wrong}/{others}")


def does(endpoint, checkpoint):
    right = good = doubted_good = 0
    for asked, made, wanted in DOES:
        state = f"Asked for: {asked}\nThe assistant says it made: {made}"
        p = ask(endpoint, checkpoint, state, {"does": {"type": "noul", "instructions": "Does what was made do what was asked for?"}})["does"]["noul"]
        doubted = p < DOUBTED
        right += doubted != wanted
        good += wanted
        doubted_good += doubted and wanted
        print(f"{'✓' if doubted != wanted else '✗'} {asked:32} {made[:56]:56} {p:.2f}{' doubted' if doubted else ''}")
    print(f"doubt right: {right}/{len(DOES)}; good work doubted: {doubted_good}/{good}")


def triage(cli):
    first = ordered = 0
    slowest = 0.0
    with tempfile.TemporaryDirectory() as folder:
        for name, text, causes in TRIAGE:
            path = os.path.join(folder, name + ".fbks")
            with open(path, "w") as patch:
                patch.write(text)
            start = time.time()
            run = subprocess.run([cli, "check", path, "--triage", "--json"], capture_output=True, text=True)
            slowest = max(slowest, time.time() - start)
            try:
                issues = json.loads(run.stdout)["issues"]
            except json.JSONDecodeError:
                print(f"  ! {name}: {run.stderr.strip() or 'no answer'}")
                continue
            if run.stderr.strip():
                print(f"  ! {name}: {run.stderr.strip()[:120]}")
            modules = [i.get("module") or "?" for i in issues]
            hit = bool(modules) and modules[0] in causes
            causal = [i for i, m in enumerate(modules) if m in causes]
            harmless = [i for i, m in enumerate(modules) if m not in causes]
            well = bool(causal) and (not harmless or max(causal) < min(harmless))
            first += hit
            ordered += well
            print(f"{'✓' if hit else '✗'}{'✓' if well else '✗'} {name:26} {[(m, i.get('likely')) for m, i in zip(modules, issues)]}")
    print(f"cause first {first}/{len(TRIAGE)}, every cause before every harmless complaint {ordered}/{len(TRIAGE)}, slowest {slowest:.1f}s")


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "find":
        find(sys.argv[2])
    elif len(sys.argv) >= 3 and sys.argv[1] == "triage":
        triage(sys.argv[2])
    elif len(sys.argv) >= 3 and sys.argv[1] in ("route", "does"):
        (route if sys.argv[1] == "route" else does)(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
    else:
        sys.exit("usage: decide-bench.py find FLYBACK_CLI | triage FLYBACK_CLI | route ENDPOINT [CHECKPOINT] | does ENDPOINT [CHECKPOINT]")
