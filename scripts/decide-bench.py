#!/usr/bin/env python3
# Measures the decision model's two uses that have a right answer against a fixed set:
# finding a module by a phrase, and reading a message to the assistant as a module question.
#
#   decide-bench.py find  FLYBACK_CLI           the module search, through `modules --find`
#   decide-bench.py route ENDPOINT [CHECKPOINT] the routing question, asked of a System One endpoint
#
# `find` asks whatever model the settings choose; run it under XDG_CONFIG_HOME pointing at a
# folder whose Flyback/settings.json names the model, to leave the real settings alone.
# `route` posts TurnReading's question straight to ENDPOINT (e.g. http://localhost:8000),
# naming CHECKPOINT (english, typed-decisions) for a laya-serve. Standard library only.

import json
import subprocess
import sys
import time
import urllib.request

FIND = {
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

# True where the assistant should be told to answer rather than build.
ROUTE = {
    "what does the ADSR do?": True,
    "how do I use the Filter?": True,
    "what is a Slew for?": True,
    "explain the Kaleidoscope module": True,
    "make the bass slower": False,
    "add some reverb": False,
    "why is it so quiet?": False,
    "start over with a drum loop": False,
    "what is the capital of France?": False,
    "load the Plasma preset": False,
    "turn the saw down and add a filter sweep": False,
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
PATCH = "A patch of 6 modules and 7 wires: Output, Sine, Saw, Filter, ADSR, Mixer."


def find(cli):
    top1 = top3 = empty = 0
    slowest = 0.0
    for phrase, wanted in FIND.items():
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
        empty += not found
        print(f"{'✓' if hit1 else '~' if hit3 else '✗'} {phrase:42} {found[:3]}")
    n = len(FIND)
    print(f"top-1 {top1}/{n}, top-3 {top3}/{n}, nothing found {empty}/{n}, slowest {slowest:.1f}s")


def route(endpoint, checkpoint):
    caught = wrong = 0
    for message, wanted in ROUTE.items():
        body = {
            "state": f"The patch: {PATCH}\nThe message: {message}",
            "questions": {"intent": {"type": "choice", "instructions": "What does the message ask for?", "criteria": INTENTS}},
        }
        if checkpoint:
            body["model"] = checkpoint
        request = urllib.request.Request(
            endpoint.rstrip("/") + "/v1/systemone",
            data=json.dumps(body).encode(),
            headers={"content-type": "application/json"})
        try:
            answer = json.load(urllib.request.urlopen(request, timeout=120))["answers"]["intent"]
        except OSError as nothing:
            sys.exit(f"{endpoint} did not answer ({nothing}). Is laya-serve running?")
        p = answer["probabilities"][answer["choice"]]
        acts = answer["choice"] == "module" and p >= SURE
        caught += acts and wanted
        wrong += acts and not wanted
        print(f"{'✓' if acts == wanted else '✗'} {message:45} {answer['choice']} {p:.2f}")
    print(f"module questions told to answer: {caught}/{sum(ROUTE.values())}; others wrongly told: {wrong}/{len(ROUTE) - sum(ROUTE.values())}")


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "find":
        find(sys.argv[2])
    elif len(sys.argv) >= 3 and sys.argv[1] == "route":
        route(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
    else:
        sys.exit("usage: decide-bench.py find FLYBACK_CLI | route ENDPOINT [CHECKPOINT]")
