"""The soundtrack: 128 BPM synthwave in A minor, synthesized from nothing. Writes music.wav and music.npy."""
import math
import os
import numpy as np
from numba import njit
from scipy.signal import butter, sosfilt, fftconvolve
from common import *

N = int(DUR * SR)
rng = np.random.default_rng(7)


# ---------------------------------------------------------------- primitives

@njit(cache=True)
def blep(t, dt):
    if t < dt:
        t /= dt
        return t + t - t * t - 1.0
    if t > 1.0 - dt:
        t = (t - 1.0) / dt
        return t * t + t + t + 1.0
    return 0.0


@njit(cache=True)
def saw(freq, phase0):
    n = freq.shape[0]
    out = np.empty(n)
    p = phase0
    for i in range(n):
        dt = freq[i] / 48000.0
        out[i] = 2.0 * p - 1.0 - blep(p, dt)
        p += dt
        if p >= 1.0:
            p -= 1.0
    return out


@njit(cache=True)
def svf(x, cutoff, q, mode):
    """Zavalishin TPT state-variable filter. mode 0 lp, 1 bp, 2 hp."""
    n = x.shape[0]
    out = np.empty(n)
    ic1 = 0.0
    ic2 = 0.0
    k = 1.0 / q
    for i in range(n):
        fc = min(cutoff[i], 20000.0)
        g = math.tan(math.pi * fc / 48000.0)
        a1 = 1.0 / (1.0 + g * (g + k))
        a2 = g * a1
        a3 = g * a2
        v3 = x[i] - ic2
        v1 = a1 * ic1 + a2 * v3
        v2 = ic2 + a2 * ic1 + a3 * v3
        ic1 = 2.0 * v1 - ic1
        ic2 = 2.0 * v2 - ic2
        if mode == 0:
            out[i] = v2
        elif mode == 1:
            out[i] = v1
        else:
            out[i] = x[i] - k * v1 - v2
    return out


@njit(cache=True)
def limiter(l, r, ceiling, release):
    n = l.shape[0]
    look = 96
    gain = np.ones(n)
    # instantaneous required gain
    req = np.empty(n)
    for i in range(n):
        p = max(abs(l[i]), abs(r[i]))
        req[i] = ceiling / p if p > ceiling else 1.0
    # min over lookahead window, then smooth release
    g = 1.0
    rel = math.exp(-1.0 / (release * 48000.0))
    for i in range(n):
        m = 1.0
        for j in range(i, min(n, i + look)):
            if req[j] < m:
                m = req[j]
        if m < g:
            g = m
        else:
            g = m + (g - m) * rel
        gain[i] = g
    return l * gain, r * gain


def midi_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def tt(n):
    return np.arange(n) / SR


def place(buf, sig, t0, gain=1.0):
    i0 = int(round(t0 * SR))
    if i0 >= buf.shape[-1]:
        return
    n = min(sig.shape[-1], buf.shape[-1] - i0)
    buf[..., i0:i0 + n] += sig[..., :n] * gain


def sos(kind, f, order=2):
    return butter(order, f, btype=kind, fs=SR, output="sos")


def noise(n):
    return rng.standard_normal(n)


def stereo(x, pan=0.0):
    a = (pan + 1) * math.pi / 4
    return np.stack([x * math.cos(a), x * math.sin(a)]) * math.sqrt(2)


# ---------------------------------------------------------------- voices

def kick(big=False):
    n = int(0.7 * SR)
    t = tt(n)
    f = 46 + 130 * np.exp(-t / 0.03) + 60 * np.exp(-t / 0.004)
    ph = 2 * np.pi * np.cumsum(f) / SR
    env = np.exp(-t / (0.42 if big else 0.30)) * np.minimum(1, t / 0.0008)
    body = np.sin(ph) * env
    click = sosfilt(sos("highpass", 1500), noise(n)) * np.exp(-t / 0.0025) * 0.35
    return np.tanh(1.8 * (body + click)) * 0.95


def clap():
    n = int(0.6 * SR)
    t = tt(n)
    e = np.zeros(n)
    for d in (0.0, 0.010, 0.021):
        m = t >= d
        e[m] += np.exp(-(t[m] - d) / 0.005)
    m = t >= 0.03
    e[m] += 0.8 * np.exp(-(t[m] - 0.03) / 0.13)
    x = sosfilt(sos("bandpass", [900, 4200]), noise(n)) * e
    body = np.sin(2 * np.pi * 195 * t) * np.exp(-t / 0.05) * 0.4
    return (x * 0.9 + body) * 0.8


def snare(level=1.0):
    n = int(0.25 * SR)
    t = tt(n)
    x = sosfilt(sos("highpass", 1200), noise(n)) * np.exp(-t / 0.06)
    body = np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.04)
    return (x * 0.7 + body * 0.5) * level


METAL = [205.3, 304.4, 369.6, 522.7, 540.0, 800.0]


def hat(open_=False, level=1.0):
    n = int((0.45 if open_ else 0.09) * SR)
    t = tt(n)
    sq = sum(np.sign(np.sin(2 * np.pi * f * 1.6 * t + i)) for i, f in enumerate(METAL))
    x = sosfilt(sos("highpass", 7200, 4), sq * 0.25 + noise(n) * 0.6)
    env = np.exp(-t / (0.16 if open_ else 0.022))
    return x * env * 0.35 * level


def crash(length=2.2):
    n = int(length * SR)
    t = tt(n)
    sq = sum(np.sign(np.sin(2 * np.pi * f * 2.3 * t + i)) for i, f in enumerate(METAL))
    out = []
    for _ in range(2):
        x = sosfilt(sos("highpass", 3800, 4), noise(n) * 0.8 + sq * 0.12)
        x = sosfilt(sos("lowpass", 12000), x)
        out.append(x * np.exp(-t / 0.55) * np.minimum(1, t / 0.002))
    return np.stack(out) * 0.3


def pluck(m, dur, cutoff, level=1.0):
    n = int((dur + 0.4) * SR)
    t = tt(n)
    f = np.full(n, midi_hz(m))
    x = saw(f, rng.random()) * 0.6 + saw(f * 1.0045, rng.random()) * 0.4
    fc = 180 + cutoff * np.exp(-t / 0.11)
    y = svf(x, fc, 1.4, 0)
    env = np.exp(-t / 0.22) * np.minimum(1, t / 0.002)
    return y * env * level


def bass_note(m, dur, bright=1.0):
    n = int((dur + 0.05) * SR)
    t = tt(n)
    f = np.full(n, midi_hz(m))
    x = saw(f, 0.0) * 0.7 + np.sin(2 * np.pi * np.cumsum(f * 0.5) / SR) * 0.9
    fc = 110 + 1700 * bright * np.exp(-t / 0.075)
    y = svf(x, fc, 1.1, 0)
    env = np.minimum(1, t / 0.003) * np.clip((dur - t) / 0.02, 0, 1) * (0.75 + 0.25 * np.exp(-t / 0.1))
    return y * env


def supersaw(notes, dur, cutoff=5200, voices=7, spread=24, attack=0.012):
    n = int((dur + 0.08) * SR)
    t = tt(n)
    L = np.zeros(n)
    R = np.zeros(n)
    det = np.linspace(-spread, spread, voices)
    for m in notes:
        for j, c in enumerate(det):
            f = np.full(n, midi_hz(m) * 2 ** (c / 1200))
            x = saw(f, rng.random())
            pan = (j / (voices - 1)) * 2 - 1
            L += x * (1 - pan) * 0.5
            R += x * (1 + pan) * 0.5
    s = np.stack([L, R]) / (voices * len(notes)) * 2.2
    s = sosfilt(sos("lowpass", cutoff), s)
    s = sosfilt(sos("highpass", 160), s)
    env = np.minimum(1, t / attack) * np.clip((dur + 0.06 - t) / 0.06, 0, 1)
    return s * env


def pop(m):
    n = int(0.6 * SR)
    t = tt(n)
    f = midi_hz(m) * (1 + 0.5 * np.exp(-t / 0.006))
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.12 * np.sin(3 * ph)
    return x * np.exp(-t / 0.16) * np.minimum(1, t / 0.001) * 0.35


def riser(length, f0=300, f1=9000):
    n = int(length * SR)
    t = tt(n)
    k = t / length
    fc = f0 * (f1 / f0) ** (k ** 1.4)
    L = svf(noise(n), fc, 2.5, 1)
    R = svf(noise(n), fc * 1.02, 2.5, 1)
    tone_f = 180 * (8.0 ** (k ** 1.6))
    tone = np.sin(2 * np.pi * np.cumsum(tone_f) / SR) * 0.25
    g = k ** 2.2
    return np.stack([L + tone, R + tone]) * g * 0.55


def whoosh(length):
    n = int(length * SR)
    t = tt(n)
    k = t / length
    fc = 5000 * (0.07 ** k)
    L = svf(noise(n), fc, 1.6, 1)
    R = svf(noise(n), fc * 1.05, 1.6, 1)
    g = np.sin(np.pi * k) ** 1.5
    return np.stack([L, R]) * g * 0.5


def sub_boom():
    n = int(2.2 * SR)
    t = tt(n)
    f = 32 + 40 * np.exp(-t / 0.35)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.7) * np.minimum(1, t / 0.004)
    return np.tanh(1.4 * x) * 0.8


def power_on():
    n = int(1.0 * SR)
    t = tt(n)
    thunk = np.sin(2 * np.pi * np.cumsum(52 + 60 * np.exp(-t / 0.02)) / SR) * np.exp(-t / 0.25)
    crackle = sosfilt(sos("bandpass", [600, 5000]), noise(n) * (rng.random(n) > 0.985)) * np.exp(-t / 0.12) * 2.5
    whine = np.sin(2 * np.pi * 7800 * t) * np.exp(-t / 0.6) * np.minimum(1, t / 0.2) * 0.012
    return thunk * 0.8 + crackle * 0.4 + whine


def retrace_tick():
    n = int(0.08 * SR)
    t = tt(n)
    x = np.sin(2 * np.pi * 2600 * t) * np.exp(-t / 0.006) + sosfilt(sos("highpass", 4000), noise(n)) * np.exp(-t / 0.002) * 0.4
    return x * 0.16


def zap_down():
    n = int(0.6 * SR)
    t = tt(n)
    f = 40 + 2200 * np.exp(-t / 0.07)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.18)
    click = sosfilt(sos("highpass", 2000), noise(n)) * np.exp(-t / 0.004) * 0.3
    return (x + click) * 0.45


def sweep_up(length):
    n = int(length * SR)
    t = tt(n)
    k = t / length
    f = 260 * (5.0 ** k)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.3 * np.sin(4 * np.pi * np.cumsum(f) / SR)
    return x * np.sin(np.pi * np.clip(k, 0, 1)) ** 0.5 * 0.12


def reverb_ir(length=2.6, decay=0.42):
    n = int(length * SR)
    t = tt(n)
    ir = []
    for _ in range(2):
        x = noise(n) * np.exp(-t / decay)
        x = sosfilt(sos("lowpass", 5500), x)
        x[: int(0.018 * SR)] = 0
        ir.append(x / np.sqrt(np.sum(x ** 2)))
    return np.stack(ir)


def pingpong(x, delay, fb, n_taps=7):
    """Stereo ping-pong from a mono send."""
    out = np.zeros((2, x.shape[-1]))
    d = int(delay * SR)
    for k in range(1, n_taps + 1):
        seg = x[: max(0, x.shape[-1] - k * d)] * (fb ** (k - 1))
        seg = sosfilt(sos("lowpass", 5000 - 400 * k), seg)
        out[(k + 1) % 2, k * d: k * d + seg.shape[-1]] += seg
    return out


# ---------------------------------------------------------------- the score

def arp_events():
    """(time, midi, velocity) for every sixteenth of the arpeggio."""
    ev = []
    s16 = B / 4
    pattern = [0, 1, 2, 3, 4, 3, 2, 1]
    for i in range(int(30 * 4)):
        t = i * s16
        beat = i / 4
        if 22.75 <= beat < 24:        # the logo draw is the arp's one rest
            continue
        name = chord_at_beat(beat)
        tones = CHORDS[name]["notes"] + [CHORDS[name]["notes"][0] + 12]
        m = tones[pattern[i % 8]]
        if beat >= 16:
            m += 12
        vel = 1.0 if i % 4 == 0 else 0.72 if i % 2 == 0 else 0.55
        if beat >= 28:
            vel *= max(0.0, 1 - (beat - 28) / 2.2)
        ev.append((t, m, vel))
    return ev


def arp_cutoff(t):
    if t < S_PATCH:
        return 900 + 2400 * (t / S_PATCH) ** 1.3
    if t < S_DROP:
        return 3100 + 1400 * lin(t, S_PATCH, S_DROP)
    if t < S_LOGO + 4 * B:
        return 5200
    return 5200 * (1 - 0.8 * lin(t, S_LOGO + 4 * B, DUR))


def bass_events():
    ev = []
    s8 = B / 2
    for i in range(int(32 * 2)):
        t = i * s8
        beat = i / 2
        if not (8 <= beat < 15 or 16 <= beat < 23 or 24 <= beat < 28):
            continue
        root = CHORDS[chord_at_beat(beat)]["bass"]
        m = root + (12 if i % 2 else 0)
        ev.append((t, m, s8 * 0.92, 1.0 if beat >= 16 else 0.7))
    ev.append((S_LOGO + 4 * B, 33, 1.7, 0.5))
    return ev


LEAD = [  # (beat, midi, beats)
    (16, 76, 1), (17, 81, .5), (17.5, 79, .5), (18, 76, 1), (19, 74, .5), (19.5, 72, .5),
    (20, 72, .5), (20.5, 74, .5), (21, 76, 1), (22, 77, .5), (22.5, 76, .25),
    (24, 81, 1.5), (25.5, 79, .5), (26, 76, 1), (27, 74, .5), (27.5, 76, .5), (28, 76, 2.5),
]


def lead_note(m, dur):
    n = int((dur + 0.5) * SR)
    t = tt(n)
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.15) / 0.2, 0, 1)
    f = midi_hz(m) * vib
    x = saw(f, 0.0) * 0.5 + saw(f * 1.006, 0.3) * 0.5
    fc = 900 + 3200 * np.exp(-t / 0.18)
    y = svf(x, fc, 1.2, 0)
    env = np.minimum(1, t / 0.006) * np.clip((dur - t) / 0.05 + 1, 0, 1) * (0.7 + 0.3 * np.exp(-t / 0.2))
    return y * env


def render():
    mix = np.zeros((2, N))
    rv_send = np.zeros((2, N))
    dl_send = np.zeros(N)
    drums = np.zeros((2, N))
    synths = np.zeros((2, N))   # ducked by the kick

    # power on and the beam's retrace ticks
    place(mix, stereo(power_on()), 0.0)
    for i in range(8):
        place(mix, stereo(retrace_tick(), 0.3 * (1 if i % 2 else -1)), (i + 1) * B - 0.04)

    # pad under the intro and the patch
    for bar in range(4):
        name = ["Am", "F", "C", "G"][bar]
        p = supersaw(CHORDS[name]["notes"], BAR, cutoff=1100 + 500 * bar, voices=3, spread=12, attack=0.35)
        place(synths, p, bar * BAR, 0.9 if bar < 2 else 0.35)
        place(rv_send, p, bar * BAR, 0.5 if bar < 2 else 0.25)

    # arpeggio
    arp = np.zeros(N)
    for t, m, v in arp_events():
        place(arp, pluck(m, B / 4, arp_cutoff(t)), t, v * (0.5 if t < S_PATCH else 0.32))
    place(synths, stereo(arp, -0.1), 0.0)
    dl_send += arp * 0.5

    # module pops
    for i, (t, m) in enumerate(zip(MODULE_POPS, POP_NOTES)):
        p = pop(m)
        place(mix, stereo(p, -0.5 + i / 6), t, 0.8)
        place(dl_send, p, t, 0.5)

    # lead hook
    lead = np.zeros(N)
    for b, m, d in LEAD:
        place(lead, lead_note(m, d * B * 0.95), b * B, 0.3)
    synths += stereo(lead, 0.12)
    dl_send += lead * 0.35
    rv_send += stereo(lead) * 0.2

    # bass
    bass = np.zeros(N)
    for t, m, d, b in bass_events():
        place(bass, bass_note(m, d, b), t, 0.55)
    synths += stereo(bass) * 0.8

    # supersaw chords for the drop and the lockup
    for beat in range(16, 32):
        if beat == 23:
            continue
        name = chord_at_beat(beat)
        t = beat * B
        if beat >= 28:
            if beat == 28:
                s = supersaw(CHORDS["Am"]["notes"] + [71], DUR - t, cutoff=3800, attack=0.02)
                s *= np.exp(-tt(s.shape[-1]) / 0.9)
                place(synths, s, t, 0.5)
                place(rv_send, s, t, 0.4)
            continue
        s = supersaw(CHORDS[name]["notes"], B, cutoff=5600)
        place(synths, s, t, 0.42)
        place(rv_send, s, t, 0.12)

    # drums
    for k in KICKS:
        place(drums, stereo(kick(big=(k == S_LOGO))), k, 0.9)
    for c in CLAPS:
        cl = clap()
        place(drums, stereo(cl, 0.05), c, 0.55)
        place(rv_send, stereo(cl), c, 0.35)
    for c in CRASHES:
        place(drums, crash(), c, 0.55)
        place(rv_send, crash(), c, 0.15)
    place(drums, crash(1.6), S_LOGO + 4 * B, 0.28)
    # hats
    for i in range(32 * 4):
        beat = i / 4
        t = i * B / 4
        if 8 <= beat < 15:
            if i % 4 == 2:
                place(drums, stereo(hat(), 0.3), t, 0.8)
            elif beat >= 12 and i % 2 == 1:
                place(drums, stereo(hat(), 0.35), t, 0.35)
        elif 16 <= beat < 23 or 24 <= beat < 28:
            if i % 4 == 2:
                place(drums, stereo(hat(open_=True), 0.25), t, 0.7)
            else:
                place(drums, stereo(hat(), 0.35), t, 0.55 if i % 2 == 0 else 0.35)
    # snare rolls into the drop and into the logo
    for i in range(8):
        t = 14 * B + i * B / 4
        place(drums, stereo(snare(0.25 + 0.5 * i / 7)), t, 0.6)
    for i in range(8):
        t = 15 * B + i * B / 8
        place(drums, stereo(snare(0.5 + 0.5 * i / 7)), t, 0.6)
    for i in range(4):
        t = 22 * B + i * B / 4
        place(drums, stereo(snare(0.4 + 0.2 * i / 3)), t, 0.55)
    for i in range(8):
        t = 23 * B + i * B / 8
        place(drums, stereo(snare(0.6 + 0.4 * i / 7)), t, 0.55)

    # risers, impacts, the logo's sweep, the switch-off
    place(mix, whoosh(0.5), MODULE_POPS[6] + 0.0, 0.7)
    place(mix, riser(S_DROP - 12 * B), 12 * B, 0.8)
    place(mix, riser(S_LOGO - 22 * B, 500, 11000), 22 * B, 0.8)
    rev = crash(B * 1.0)[:, ::-1]
    place(mix, rev, S_LOGO - B, 0.9)
    place(mix, stereo(sub_boom()), S_LOGO, 0.9)
    place(mix, stereo(sub_boom()), S_DROP, 0.55)
    place(mix, stereo(sweep_up(B * 0.95)), S_LOGO_DRAW, 1.0)
    place(mix, stereo(zap_down()), S_OFF, 1.0)

    # sidechain: every kick ducks the synths
    t = tt(N)
    duck = np.ones(N)
    for k in KICKS:
        i0 = int(k * SR)
        n = int(0.4 * SR)
        seg = t[:n]
        g = 1 - 0.72 * np.exp(-seg / 0.11) * np.minimum(1, seg / 0.004 + 0.3)
        e = min(N, i0 + n)
        duck[i0:e] = np.minimum(duck[i0:e], g[: e - i0])
    synths *= duck

    # delay and reverb
    dl = pingpong(dl_send, 0.75 * B, 0.42)
    rv_in = rv_send + dl * 0.3
    ir = reverb_ir()
    rv = np.stack([fftconvolve(rv_in[0], ir[0])[:N], fftconvolve(rv_in[1], ir[1])[:N]])

    mix += drums + synths + dl * 0.55 + rv * 0.5
    mix = sosfilt(sos("highpass", 28), mix)

    # end: a hard stop just before 15 s, after the zap
    fade = np.clip((DUR - t) / 0.12, 0, 1)
    mix *= fade

    return mix


def master(mix, target_lufs=-13.0):
    import pyloudnorm as pyln
    meter = pyln.Meter(SR)
    # gentle glue, then level to target, then limit
    mix = np.tanh(mix * 0.9) / 0.9
    lufs = meter.integrated_loudness(mix.T)
    mix *= 10 ** ((target_lufs - lufs) / 20)
    L, R = limiter(np.ascontiguousarray(mix[0]), np.ascontiguousarray(mix[1]), 0.89, 0.08)
    out = np.stack([L, R])
    print("loudness in", round(lufs, 2), "-> out", round(meter.integrated_loudness(out.T), 2),
          "peak", round(20 * np.log10(np.abs(out).max()), 2), "dBFS")
    return out


if __name__ == "__main__":
    import soundfile as sf
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    out = master(render())
    sf.write("music.wav", out.T.astype(np.float32), SR, subtype="PCM_24")
    np.save("music.npy", out.astype(np.float32))
    print("wrote music.wav", out.shape)
