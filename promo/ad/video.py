"""Renders the ad: skia draws the vectors, the GPU does the light. `python video.py` writes the film;
`python video.py 1.2 5.0 ...` writes stills at those seconds for checking."""
import math
import os
import subprocess
import sys

os.chdir(os.path.dirname(os.path.abspath(__file__)))
if not os.path.exists("music.npy"):
    subprocess.run([sys.executable, "music.py"], check=True)
import numpy as np
import moderngl
import skia
from common import *
import music
import overlay as ov
from shaders import VERT, FEEDBACK, COMPOSITE, PREFILTER, DOWN, UP, FINAL

ctx = moderngl.create_standalone_context(require=330)
quad = ctx.buffer(np.array([-1, -1, 1, -1, -1, 1, 1, 1], "f4").tobytes())


def program(frag):
    p = ctx.program(vertex_shader=VERT, fragment_shader=frag)
    vao = ctx.vertex_array(p, [(quad, "2f", "pos")])
    return p, vao


P_FB, V_FB = program(FEEDBACK)
P_COMP, V_COMP = program(COMPOSITE)
P_PRE, V_PRE = program(PREFILTER)
P_DOWN, V_DOWN = program(DOWN)
P_UP, V_UP = program(UP)
P_FIN, V_FIN = program(FINAL)


def tex(w, h, comps=4, dtype="f2"):
    t = ctx.texture((w, h), comps, dtype=dtype)
    t.filter = (moderngl.LINEAR, moderngl.LINEAR)
    t.repeat_x = False
    t.repeat_y = False
    return t


fb_tex = [tex(W, H), tex(W, H)]
fb_fbo = [ctx.framebuffer([t]) for t in fb_tex]
for f in fb_fbo:
    f.use(); ctx.clear(0, 0, 0, 1)
comp_tex = tex(W, H)
comp_fbo = ctx.framebuffer([comp_tex])
ui_tex = tex(W, H, 4, "f1")
audio_tex = ctx.texture((2048, 2), 1, dtype="f4")
audio_tex.filter = (moderngl.LINEAR, moderngl.LINEAR)
audio_tex.repeat_x = True
LEVELS = 6
down_tex = [tex(W >> (i + 1), H >> (i + 1)) for i in range(LEVELS)]
down_fbo = [ctx.framebuffer([t]) for t in down_tex]
up_tex = [tex(W >> (i + 1), H >> (i + 1)) for i in range(LEVELS)]
up_fbo = [ctx.framebuffer([t]) for t in up_tex]
out_tex = ctx.texture((W, H), 3, dtype="f1")
out_fbo = ctx.framebuffer([out_tex])

ui_arr = np.zeros((H, W, 4), np.uint8)
ui_surf = skia.Surface(ui_arr)
ui_canvas = ui_surf.getCanvas()

AUDIO = np.load("music.npy")
ARP = music.arp_events()
LEAD = [(b * B, m, d * B) for b, m, d in music.LEAD]
DECAYS = [0.86, 0.9, 0.84, 0.78, 0.4, 0.86, 0.5]


def u(p, name, value):
    if name not in p:
        return
    m = p[name]
    if isinstance(value, np.ndarray):
        m.write(value.astype("f4").tobytes())
    else:
        m.value = value


def kick_env(t):
    return min(1.0, env_since(t, KICKS, 0.11))


def flash_env(t):
    v = 0.0
    for e, a in ((S_PATCH, 0.35), (S_DROP, 1.0), (S_LOGO, 0.9)):
        if t >= e:
            v += a * math.exp(-(t - e) / 0.022)
    return v


def tube(t):
    glow = 0.0
    if t < 0.07:
        x, y = max(0.004, ease_out_cubic(t / 0.07)), 0.003
        glow = 1.2
    elif t < 0.4:
        k = ease_out_expo(lin(t, 0.07, 0.4))
        x, y = 1.0, max(0.003, k)
        glow = 1.2 * (1 - k) ** 8
    elif t < S_OFF:
        x, y = 1.0, 1.0
    elif t < S_OFF + 0.11:
        k = ease_in_expo(lin(t, S_OFF, S_OFF + 0.11))
        x, y = 1.0, max(0.003, 1 - k)
        glow = 1.2 * k ** 4
    elif t < S_OFF + 0.22:
        k = ease_in_out(lin(t, S_OFF + 0.11, S_OFF + 0.22))
        x, y = max(0.0025, 1 - k), 0.003
        glow = 1.2 + 1.5 * k
    else:
        k = lin(t, S_OFF + 0.22, S_OFF + 0.42)
        x, y = 0.0025, 0.004
        glow = 2.7 * (1 - k) ** 2
    return x, y, glow


def seq_uniforms(t):
    s16 = B / 4
    lit = np.zeros(16)
    row = np.zeros(16)
    by_time = {round(e[0] / s16): e for e in ARP}
    cur = int(t / s16)
    for j in range(16):
        # the most recent sixteenth in column j
        k = cur - ((cur - j) % 16)
        tj = k * s16
        e = by_time.get(k)
        if e is None or tj < S_DROP - BAR:
            continue
        lit[j] = math.exp(-(t - tj) / 0.4)
        row[j] = 7 - clamp(round((e[1] - 69) / 3.43), 0, 7)
    return lit, row


def note_uniforms(t):
    out = []
    for et, m, v in ARP:
        if t - 0.4 < et < t + 1.3:
            out.append((m, et - t, B / 4 * 0.8, 0.0))
    for et, m, d in LEAD:
        if et - 0.1 < t + 1.3 and et + d > t - 0.2:
            out.append((m, et - t, d * 0.95, 1.0))
    out.sort(key=lambda n: n[1])
    out = out[:32]
    arr = np.zeros((32, 4))
    for i, n in enumerate(out):
        arr[i] = n
    return arr, len(out)


def beam_points(t, dt):
    pts = np.zeros((96, 4))
    cols = np.zeros((96, 3))
    n = 0
    if t < S_PATCH - 0.03:
        steps = 64
        prev = None
        for i in range(steps):
            ti = t - dt + dt * (i + 1) / steps
            bp = ov.beam_pos(ti)
            if bp is None:
                continue
            x, y, ph = bp
            sp = math.hypot(x - prev[0], y - prev[1]) if prev else 2.0
            prev = (x, y)
            if sp > 60:      # the bar's own retrace, left to right edge, is blanked
                continue
            pts[n] = (x, y, 0, min(0.5, 0.075 * sp))
            cols[n] = ov.beam_color(ph)
            n += 1
    elif S_LOGO_DRAW <= t < S_LOGO:
        steps = 48
        s, ox, oy = ov.mark_transform(t)
        prev = None
        for i in range(steps):
            ti = t - dt + dt * (i + 1) / steps
            if ti < S_LOGO_DRAW + 0.03:
                continue
            hx, hy = ov.mark_head(ti)
            x, y = ox + hx * s, oy + hy * s
            sp = math.hypot(x - prev[0], y - prev[1]) if prev else 2.0
            prev = (x, y)
            if sp > 40:
                continue
            pts[n] = (x, y, 0, min(0.5, 0.06 * sp))
            p = ov.mark_draw_progress(ti)
            cols[n] = (0.9, 0.12, 0.12) if 0.4 <= p < 0.58 else ov.beam_color(0.9 * min(1, (p if p < 0.4 else (p - 0.58) / 0.42 * 0.8)))
            n += 1
    return pts, cols, n


def render_frame(t, dt, cur):
    kick = kick_env(t)
    # --- vectors
    ov.draw(ui_canvas, t)
    ui_tex.write(ui_arr.tobytes())
    # --- audio window
    i1 = int(t * SR)
    win = np.zeros((2, 2048), np.float32)
    i0 = max(0, i1 - 2048)
    if i1 > 0:
        seg = AUDIO[:, i0:i1]
        win[:, 2048 - seg.shape[1]:] = seg
    audio_tex.write(win.tobytes())

    if t < S_PATCH:
        scene = 1
    elif t < S_DROP:
        scene = 2
    elif t < S_LOGO_DRAW:
        scene = 3
    elif t < S_LOGO:
        scene = 4
    else:
        scene = 5
    idx = min(6, int((t - S_DROP) / B)) if scene == 3 else 0

    lit, row = seq_uniforms(t)
    notes, nn = note_uniforms(t)
    for p in (P_FB, P_COMP):
        u(p, "res", (float(W), float(H)))
        u(p, "seqLit", lit)
        u(p, "seqRow", row)
        u(p, "notes", notes)
        u(p, "nNotes", nn)
        u(p, "audioTex", 3)
        u(p, "t", t)
        u(p, "kick", kick)
    audio_tex.use(3)

    # --- feedback
    pts, cols, n = beam_points(t, dt)
    src, dst = cur, 1 - cur
    fb_tex[src].use(0)
    u(P_FB, "prev", 0)
    if scene == 1:
        mode, decay, zoom, rot = 0, 0.988, 1.0, 0.0
    elif scene == 2:
        mode, decay, zoom, rot = 0, 0.75, 1.0, 0.0
    elif scene == 3:
        mode, decay = 1, DECAYS[idx]
        zoom, rot = 1.012 + 0.03 * kick, 0.004
        if t - S_DROP < dt:
            decay = 0.0
    elif scene == 4:
        mode, decay, zoom, rot = 0, (0.0 if t - S_LOGO_DRAW < dt * 1.01 else 0.95), 1.0, 0.0
    else:
        mode, decay, zoom, rot = 0, 0.86, 1.0, 0.0
    u(P_FB, "mode", mode)
    u(P_FB, "presetId", idx)
    u(P_FB, "decay", decay)
    u(P_FB, "fbZoom", zoom)
    u(P_FB, "fbRot", rot)
    u(P_FB, "beam", pts)
    u(P_FB, "beamCol", cols)
    u(P_FB, "nBeam", n)
    fb_fbo[dst].use()
    V_FB.render(moderngl.TRIANGLE_STRIP)

    # --- composite
    fb_tex[dst].use(0)
    ui_tex.use(1)
    u(P_COMP, "fbTex", 0)
    u(P_COMP, "uiTex", 1)
    u(P_COMP, "fbAmt", 1.0)
    u(P_COMP, "bgMode", 1.0 if scene == 5 else 0.0)
    u(P_COMP, "glowAmt", ease_out_cubic(lin(t, S_LOGO, S_LOGO + 1.0)))
    u(P_COMP, "scopeAmt", 0.85 if scene == 3 and idx not in (4, 6) else 0.0)
    u(P_COMP, "uiGain", 1.05 if scene >= 3 else 1.0)
    head = (0.0, 0.0, 0.0)
    if scene == 1:
        bp = ov.beam_pos(t)
        if bp:
            head = (bp[0], bp[1], 1.0)
    elif scene == 4 and t > S_LOGO_DRAW + 0.03:
        s, ox, oy = ov.mark_transform(t)
        hx, hy = ov.mark_head(t)
        head = (ox + hx * s, oy + hy * s, 1.0)
    u(P_COMP, "head", head)
    if scene == 2 and t >= MODULE_POPS[6]:
        rect, a, rad = ov.patch_preview_rect(t)
        u(P_COMP, "prevRect", tuple(float(v) for v in rect))
        u(P_COMP, "prevAlpha", a)
        u(P_COMP, "prevRadius", rad)
    else:
        u(P_COMP, "prevAlpha", 0.0)
    comp_fbo.use()
    V_COMP.render(moderngl.TRIANGLE_STRIP)

    # --- bloom
    thr = {1: 0.9, 2: 0.85, 3: 0.95, 4: 0.6, 5: 0.8}[scene]
    comp_tex.use(0)
    u(P_PRE, "src", 0)
    u(P_PRE, "res", (float(W), float(H)))
    u(P_PRE, "threshold", thr)
    down_fbo[0].use()
    V_PRE.render(moderngl.TRIANGLE_STRIP)
    for i in range(1, LEVELS):
        down_tex[i - 1].use(0)
        u(P_DOWN, "src", 0)
        u(P_DOWN, "texel", (1.0 / down_tex[i - 1].width, 1.0 / down_tex[i - 1].height))
        down_fbo[i].use()
        V_DOWN.render(moderngl.TRIANGLE_STRIP)
    # up: start from the smallest
    small = down_tex[LEVELS - 1]
    for i in range(LEVELS - 2, -1, -1):
        small.use(0)
        down_tex[i].use(1)
        u(P_UP, "src", 0)
        u(P_UP, "base", 1)
        u(P_UP, "texel", (1.0 / small.width, 1.0 / small.height))
        u(P_UP, "spread", 1.0)
        up_fbo[i].use()
        V_UP.render(moderngl.TRIANGLE_STRIP)
        small = up_tex[i]

    # --- final
    comp_tex.use(0)
    up_tex[0].use(1)
    u(P_FIN, "scene", 0)
    u(P_FIN, "bloom", 1)
    u(P_FIN, "res", (float(W), float(H)))
    u(P_FIN, "t", t)
    u(P_FIN, "bloomAmt", {1: 0.8, 2: 0.4, 3: 0.5, 4: 0.75, 5: 0.55}[scene])
    fl = flash_env(t)
    u(P_FIN, "flash", fl)
    u(P_FIN, "ca", 0.0015 + (0.006 * kick if scene == 3 else 0.002 * kick) + 0.02 * min(1, fl))
    u(P_FIN, "scan", 0.10 if scene == 1 else 0.05)
    u(P_FIN, "grain", 0.025)
    u(P_FIN, "shake", (kick * 0.8 if scene == 3 else 0.0) + min(1.0, fl) * 1.5)
    u(P_FIN, "vignette", 0.75)
    tx, ty, tg = tube(t)
    u(P_FIN, "tube", (tx, ty))
    u(P_FIN, "tubeGlow", tg)
    if S_LOGO <= t < S_LOGO + 0.7:
        tau = t - S_LOGO
        ox, oy = ov.dot_screen(S_LOGO)
        u(P_FIN, "shock", (ox, oy, 40 + 1400 * ease_out_cubic(tau / 0.7), (1 - tau / 0.7) ** 2))
    else:
        u(P_FIN, "shock", (0.0, 0.0, 0.0, 0.0))
    out_fbo.use()
    V_FIN.render(moderngl.TRIANGLE_STRIP)
    return dst


def read_frame():
    return out_fbo.read(components=3, alignment=1)


def stills(times):
    from PIL import Image
    for T in times:
        cur = 0
        for f in fb_fbo:
            f.use(); ctx.clear(0, 0, 0, 1)
        start = max(0.0, T - 1.2)
        n = int(round((T - start) * FPS))
        for i in range(n + 1):
            t = start + i / FPS
            cur = render_frame(t, 1 / FPS, cur)
        img = Image.frombytes("RGB", (W, H), read_frame()).transpose(Image.FLIP_TOP_BOTTOM)
        img.save("still_%05.2f.png" % T)
        print("still", T)


def film(path="flyback_ad.mp4"):
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H), "-r", str(FPS), "-i", "-",
           "-i", "music.wav",
           "-vf", "vflip", "-c:v", "libx264", "-preset", "slow", "-crf", "15", "-pix_fmt", "yuv420p",
           "-profile:v", "high", "-c:a", "aac", "-b:a", "320k", "-movflags", "+faststart", "-shortest", path]
    ff = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    cur = 0
    total = int(DUR * FPS)
    for i in range(total):
        t = i / FPS
        cur = render_frame(t, 1 / FPS, cur)
        ff.stdin.write(read_frame())
        if i % 60 == 0:
            print("frame", i, "/", total, flush=True)
    ff.stdin.close()
    ff.wait()
    print("wrote", path)


if __name__ == "__main__":
    if len(sys.argv) > 1:
        stills([float(a) for a in sys.argv[1:]])
    else:
        film()
