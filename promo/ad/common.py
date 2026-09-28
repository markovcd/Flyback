"""Shared timeline: 128 BPM, eight bars, exactly 15 seconds."""
import math

BPM = 128
B = 60.0 / BPM          # one beat, 0.46875 s
BAR = 4 * B
DUR = 15.0
SR = 48000
FPS = 60
W, H = 1920, 1080

# Scene boundaries, in seconds.
S_BEAM = 0.0            # bars 1-2: CRT on, beam draws the sawtooth
S_PATCH = 8 * B         # bars 3-4: modules pop in, wires draw
S_DROP = 16 * B         # bars 5-6: full-screen visuals, one word a beat
S_LOGO_DRAW = 23 * B    # beat 24: black, the beam draws the mark
S_LOGO = 24 * B         # bars 7-8: lockup
S_OFF = 14.52           # CRT switches off

# Chords per beat (root name for the picture, midi notes for the sound).
CHORDS = {
    "Am": dict(bass=33, notes=[57, 60, 64, 69]),
    "F":  dict(bass=29, notes=[53, 57, 60, 65]),
    "C":  dict(bass=36, notes=[55, 60, 64, 67]),
    "G":  dict(bass=31, notes=[55, 59, 62, 67]),
}


def chord_at_beat(b):
    b = int(b)
    bar = b // 4
    if bar == 5 and b % 4 == 3:
        return "G"
    return ["Am", "F", "C", "G", "Am", "F", "Am", "Am"][min(bar, 7)]


# Seven modules pop in on half beats through bar 3.
MODULE_POPS = [8 * B + i * 0.5 * B for i in range(7)]
POP_NOTES = [69, 72, 74, 76, 79, 81, 84]   # A minor pentatonic, climbing

KICKS = (
    [S_PATCH + i * B for i in range(7)] +             # bar 3 and most of 4
    [S_DROP + i * B for i in range(7)] +              # drop, until the logo draw
    [S_LOGO + i * B for i in range(4)]                # the lockup's groove
)
CLAPS = [S_DROP + i * B for i in (1, 3, 5)] + [S_LOGO + B, S_LOGO + 3 * B]
CRASHES = [S_DROP, S_LOGO]

WORDS = ["OSCILLATORS", "FEEDBACK", "GEOMETRY", "PATTERNS", "SEQUENCERS", "SCOPES", "MIDI"]


def clamp(x, a=0.0, b=1.0):
    return a if x < a else b if x > b else x


def lin(t, t0, t1):
    return clamp((t - t0) / (t1 - t0)) if t1 != t0 else float(t >= t0)


def ease_out_cubic(x):
    x = clamp(x)
    return 1 - (1 - x) ** 3


def ease_in_out(x):
    x = clamp(x)
    return x * x * (3 - 2 * x)


def ease_out_expo(x):
    x = clamp(x)
    return 1.0 if x >= 1 else 1 - 2 ** (-10 * x)


def ease_in_expo(x):
    x = clamp(x)
    return 0.0 if x <= 0 else 2 ** (10 * x - 10)


def ease_out_back(x, s=1.9):
    x = clamp(x)
    x -= 1
    return 1 + (s + 1) * x ** 3 + s * x ** 2


def elastic(x):
    x = clamp(x)
    if x in (0.0, 1.0):
        return x
    return 2 ** (-9 * x) * math.sin((x * 10 - 0.75) * (2 * math.pi) / 3.2) + 1


def env_since(t, times, decay):
    """Sum of exponential decays after each event time, for pulsing things on hits."""
    v = 0.0
    for e in times:
        if e <= t < e + decay * 8:
            v += math.exp(-(t - e) / decay)
    return v


def last_event(t, times):
    best = None
    for e in times:
        if e <= t:
            best = e
    return best
