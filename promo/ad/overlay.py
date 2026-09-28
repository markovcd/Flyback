"""Everything drawn as vectors: type, modules, wires, the mark, the HUD."""
import math
import numpy as np
import skia
from common import *
import music

FONTS = {
    "fonts/Inter.ttf": "https://github.com/google/fonts/raw/main/ofl/inter/Inter%5Bopsz,wght%5D.ttf",
    "fonts/JetBrainsMono.ttf": "https://github.com/google/fonts/raw/main/ofl/jetbrainsmono/JetBrainsMono%5Bwght%5D.ttf",
}


def _fetch_fonts():
    import os
    import urllib.request
    os.makedirs("fonts", exist_ok=True)
    for path, url in FONTS.items():
        if not os.path.exists(path):
            urllib.request.urlretrieve(url, path)


_fetch_fonts()
INTER = skia.Typeface.MakeFromFile("fonts/Inter.ttf")
MONO = skia.Typeface.MakeFromFile("fonts/JetBrainsMono.ttf")


def _tag(s):
    return (ord(s[0]) << 24) | (ord(s[1]) << 16) | (ord(s[2]) << 8) | ord(s[3])


_tf = {}


def typeface(weight, mono=False):
    key = (weight, mono)
    if key not in _tf:
        C = skia.FontArguments.VariationPosition.Coordinate
        coords = [C(_tag("wght"), float(weight))]
        if not mono:
            coords.append(C(_tag("opsz"), 32.0))
        fa = skia.FontArguments()
        fa.setVariationDesignPosition(skia.FontArguments.VariationPosition(
            skia.FontArguments.VariationPosition.Coordinates(coords)))
        _tf[key] = (MONO if mono else INTER).makeClone(fa)
    return _tf[key]


def font(size, weight=400, mono=False):
    f = skia.Font(typeface(weight, mono), size)
    f.setSubpixel(True)
    f.setEdging(skia.Font.Edging.kAntiAlias)
    f.setHinting(skia.FontHinting.kNone)
    return f


def hexc(h, a=1.0):
    h = h.lstrip("#")
    return skia.Color(int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), int(255 * clamp(a)))


SWEEP = [hexc("#4A9EDE"), hexc("#3FC8C8"), hexc("#4FC387")]
BRIGHT = "#E9ECF1"


def paint(color="#FFFFFF", a=1.0, stroke=None, blur=None, cap=None):
    p = skia.Paint(AntiAlias=True, Color=hexc(color, a) if isinstance(color, str) else color)
    if stroke:
        p.setStyle(skia.Paint.kStroke_Style)
        p.setStrokeWidth(stroke)
    if cap == "round":
        p.setStrokeCap(skia.Paint.kRound_Cap)
        p.setStrokeJoin(skia.Paint.kRound_Join)
    if blur:
        p.setMaskFilter(skia.MaskFilter.MakeBlur(skia.kNormal_BlurStyle, blur))
    return p


def gradient_paint(x0, x1, a=1.0, y=0):
    p = skia.Paint(AntiAlias=True)
    p.setShader(skia.GradientShader.MakeLinear([(x0, y), (x1, y)], SWEEP))
    p.setAlphaf(clamp(a))
    return p


def measure(s, f, track=0.0):
    g = f.textToGlyphs(s)
    w = f.getWidths(g)
    return sum(w) + track * (len(s) - 1), w


def text(c, s, x, y, f, color=BRIGHT, align="c", track=0.0, alpha=1.0, letter=None, grad=None, shadow=0.0):
    """Draws s with per-letter control. letter(i) -> (dx, dy, alpha, scale)."""
    total, widths = measure(s, f, track)
    x0 = x - total / 2 if align == "c" else x - total if align == "r" else x
    xs = []
    xx = x0
    for w in widths:
        xs.append(xx)
        xx += w + track
    if shadow > 0:
        sp = paint("#000000", 0.55 * alpha * shadow, blur=f.getSize() * 0.22)
        c.drawString(s, x0, y + f.getSize() * 0.04, f, sp)
    for i, ch in enumerate(s):
        if ch == " ":
            continue
        dx, dy, a, sc = letter(i) if letter else (0, 0, 1, 1)
        a *= alpha
        if a <= 0.003:
            continue
        if grad is not None:
            p = gradient_paint(grad[0], grad[1], a)
        else:
            p = paint(color, a)
        if sc != 1:
            c.save()
            cx = xs[i] + widths[i] / 2
            c.translate(cx + dx, y + dy)
            c.scale(sc, sc)
            c.drawString(ch, -widths[i] / 2, 0, f, p)
            c.restore()
        else:
            c.drawString(ch, xs[i] + dx, y + dy, f, p)
    return x0, total


def rise(t, t_in, t_out, size, stagger=0.022, dur=0.42):
    """Letters rise into place from below a mask line, and leave upward."""
    def f(i):
        a_in = ease_out_expo(lin(t, t_in + i * stagger, t_in + i * stagger + dur))
        a_out = ease_in_out(lin(t, t_out + i * 0.006, t_out + i * 0.006 + 0.16)) if t_out else 0
        dy = (1 - a_in) * size * 0.9 - a_out * size * 0.9
        return 0, dy, a_in * (1 - a_out), 1
    return f


def masked(c, x0, x1, y, size, draw):
    c.save()
    c.clipRect(skia.Rect.MakeLTRB(x0 - 40, y - size * 1.05, x1 + 40, y + size * 0.3))
    draw()
    c.restore()


# ---------------------------------------------------------------- scene 1: the beam

BEAM_X0, BEAM_X1 = 150, 1770
BEAM_Y_LO, BEAM_Y_HI = 640, 330


def beam_pos(t):
    """Where the beam is: one sweep a bar, one sawtooth a beat. Returns x, y, phase-in-beat."""
    tb = t - 0.35
    if tb < 0:
        return None
    bar_ph = (tb % BAR) / BAR
    x = BEAM_X0 + (BEAM_X1 - BEAM_X0) * bar_ph
    ph = (tb % B) / B
    ret = 0.9
    amp = 1.0 + 0.12 * math.sin(tb * 2.1)
    mid = (BEAM_Y_LO + BEAM_Y_HI) / 2
    half = (BEAM_Y_LO - BEAM_Y_HI) / 2 * amp
    if ph < ret:
        y = mid + half - 2 * half * (ph / ret)
    else:
        y = mid - half + 2 * half * ((ph - ret) / (1 - ret))
    return x, y, ph


def beam_color(ph):
    if ph >= 0.9:
        return (0.9, 0.12, 0.12)
    k = ph / 0.9
    a = [(0.069, 0.341, 0.730), (0.054, 0.578, 0.578), (0.082, 0.545, 0.238)]
    if k < 0.5:
        u = k * 2
        return tuple(a[0][j] + (a[1][j] - a[0][j]) * u for j in range(3))
    u = k * 2 - 1
    return tuple(a[1][j] + (a[2][j] - a[1][j]) * u for j in range(3))


def draw_graticule(c, t):
    a = ease_out_cubic(lin(t, 0.35, 0.9)) * (1 - lin(t, S_PATCH - 0.25, S_PATCH))
    if a <= 0:
        return
    x0, x1, y0, y1 = 130, 1790, 250, 720
    p = paint("#3FC8C8", 0.10 * a, stroke=1.2)
    for i in range(11):
        x = x0 + (x1 - x0) * i / 10
        c.drawLine(x, y0, x, y1, p)
    for j in range(7):
        y = y0 + (y1 - y0) * j / 6
        c.drawLine(x0, y, x1, y, p)
    tick = paint("#3FC8C8", 0.22 * a, stroke=1.2)
    ym = (y0 + y1) / 2
    for i in range(51):
        x = x0 + (x1 - x0) * i / 50
        c.drawLine(x, ym - 5, x, ym + 5, tick)


def draw_beam_scene(c, t):
    draw_graticule(c, t)
    f = font(118, 860)
    # TV-style on-screen label
    if 0.25 < t < 1.6:
        blink = 1.0 if (t * 4) % 1 < 0.8 or t < 1.0 else 0.0
        a = lin(t, 0.25, 0.3) * (1 - lin(t, 1.45, 1.6)) * blink
        text(c, "AV-1", 110, 120, font(40, 700, True), "#4FC387", align="l", alpha=a, track=2)
        text(c, "▶ PLAY", 110, 168, font(28, 600, True), "#4FC387", align="l", alpha=a * 0.8, track=2)

    lines = [("SEE THE ", "SOUND.", 0.45, 1.66), ("HEAR THE ", "PICTURE.", 1.93, 3.5)]
    y = 860
    for pre, key, t_in, t_out in lines:
        if not (t_in - 0.1 < t < t_out + 0.5):
            continue
        whole = pre + key
        total, _ = measure(whole, f, -3)
        x0 = 960 - total / 2
        anim = rise(t, t_in, t_out, 118)
        w_pre, _ = measure(pre, f, -3)

        # the plain part, then the key word in the sweep gradient
        def draw2():
            text(c, pre, x0, y, f, BRIGHT, align="l", track=-3, letter=anim)
            n = len(pre)
            text(c, key, x0 + w_pre + (-3), y, f, align="l", track=-3,
                 letter=lambda i: anim(i + n), grad=(x0 + w_pre, x0 + total))
        masked(c, x0, x0 + total, y, 118, draw2)


# ---------------------------------------------------------------- scene 2: the patch

MODS = [
    dict(name="Time", col="#4A9EDE", x=-760, y=-20, w=190, h=112, ins=[], outs=["t"], kind="time"),
    dict(name="Sine", col="#4FC387", x=-500, y=-200, w=200, h=130, ins=["in", "freq"], outs=["out"], kind="sine"),
    dict(name="Sequencer", col="#D8B04A", x=-500, y=120, w=220, h=140, ins=["rate"], outs=["note", "gate"], kind="seq"),
    dict(name="Kaleidoscope", col="#B484E0", x=-220, y=-240, w=220, h=150, ins=["x", "y", "segments"], outs=["x", "y"], kind="kal"),
    dict(name="Voice", col="#D87A48", x=-200, y=120, w=210, h=150, ins=["note", "gate", "cutoff"], outs=["out"], kind="voice"),
    dict(name="Feedback", col="#3FC8C8", x=90, y=-210, w=200, h=130, ins=["x", "y"], outs=["color"], kind="fb"),
    dict(name="Output", col="#E05A5A", x=380, y=-80, w=300, h=204, ins=["color", "left", "right"], outs=[], kind="out"),
]
WIRES = [  # src, out, dst, in
    (0, 0, 1, 0), (0, 0, 2, 0), (1, 0, 3, 2), (1, 0, 4, 2), (2, 0, 4, 0), (2, 1, 4, 1),
    (3, 0, 5, 0), (3, 1, 5, 1), (5, 0, 6, 0), (4, 0, 6, 1), (4, 0, 6, 2),
]
HEADER = 32
ROW0 = 58
ROWH = 28


def sock_in(m, i):
    return m["x"], m["y"] + ROW0 + i * ROWH


def sock_out(m, i):
    return m["x"] + m["w"], m["y"] + ROW0 + i * ROWH


def preview_rect(m):
    return (m["x"] + 100, m["y"] + 44, m["x"] + m["w"] - 12, m["y"] + 44 + (m["w"] - 112) * 9 / 16)


def pop_amount(t, i):
    return lin(t, MODULE_POPS[i], MODULE_POPS[i] + 0.45)


CAM_KEYS = [  # time, focus x, focus y, zoom
    (S_PATCH, -470.0, 30.0, 1.62),
    (MODULE_POPS[3], -170.0, 20.0, 1.58),
    (MODULE_POPS[6], 250.0, 10.0, 1.52),
    (MODULE_POPS[6] + 0.42, 150.0, 30.0, 0.95),
    (S_DROP - B, 150.0, 30.0, 0.99),
]


def base_camera(t):
    ks = CAM_KEYS
    if t <= ks[0][0]:
        return ks[0][3], (ks[0][1], ks[0][2])
    for a, b in zip(ks, ks[1:]):
        if t <= b[0]:
            u = ease_in_out(lin(t, a[0], b[0]))
            z = math.exp(math.log(a[3]) + (math.log(b[3]) - math.log(a[3])) * u)
            return z, (a[1] + (b[1] - a[1]) * u, a[2] + (b[2] - a[2]) * u)
    return ks[-1][3], (ks[-1][1], ks[-1][2])


def camera(t):
    """Canvas-to-screen: returns zoom and the canvas point at the screen's center."""
    z0, focus0 = base_camera(min(t, S_DROP - B))
    out = MODS[6]
    x0, y0, x1, y1 = preview_rect(out)
    pc = ((x0 + x1) / 2, (y0 + y1) / 2)
    z_end = max(W / (x1 - x0), H / (y1 - y0)) * 1.002
    u = lin(t, S_DROP - B, S_DROP) ** 2.6
    z = math.exp(math.log(z0) + (math.log(z_end) - math.log(z0)) * u)
    if u <= 0:
        return z0, focus0
    # the preview's center slides to the screen's center while the zoom grows
    s0x = (pc[0] - focus0[0]) * z0
    s0y = (pc[1] - focus0[1]) * z0
    m = ease_in_out(u ** 0.6)
    sx, sy = s0x * (1 - m), s0y * (1 - m)
    return z, (pc[0] - sx / z, pc[1] - sy / z)


def to_screen(t, x, y):
    z, (fx, fy) = camera(t)
    return W / 2 + (x - fx) * z, H / 2 + (y - fy) * z


def wire_path(a, b):
    (x1, y1), (x2, y2) = a, b
    dx = max(70, abs(x2 - x1) * 0.5)
    p = skia.Path()
    p.moveTo(x1, y1)
    p.cubicTo(x1 + dx, y1, x2 - dx, y2, x2, y2)
    return p


def draw_grid(c, t, z, f):
    step = 40
    # visible canvas bounds
    x0 = f[0] - W / 2 / z - step
    x1 = f[0] + W / 2 / z + step
    y0 = f[1] - H / 2 / z - step
    y1 = f[1] + H / 2 / z + step
    if (x1 - x0) / step > 400:
        return
    minor = paint("#24272C", 1.0, stroke=1.0 / z)
    major = paint("#2C3036", 1.0, stroke=1.4 / z)
    gx = math.floor(x0 / step) * step
    while gx < x1:
        c.drawLine(gx, y0, gx, y1, major if gx % 200 == 0 else minor)
        gx += step
    gy = math.floor(y0 / step) * step
    while gy < y1:
        c.drawLine(x0, gy, x1, gy, major if gy % 200 == 0 else minor)
        gy += step


def arp_step(t):
    s16 = B / 4
    return int(t / s16)


def draw_module(c, m, t, pop, idx):
    if pop <= 0:
        return
    s = 0.55 + 0.45 * ease_out_back(pop, 2.2)
    a = clamp(pop * 4)
    x, y, w, h = m["x"], m["y"], m["w"], m["h"]
    cx, cy = x + w / 2, y + h / 2
    c.save()
    c.translate(cx, cy)
    c.scale(s, s)
    c.translate(-cx, -cy)
    rr = skia.RRect.MakeRectXY(skia.Rect.MakeXYWH(x, y, w, h), 9, 9)
    c.drawRRect(skia.RRect.MakeRectXY(skia.Rect.MakeXYWH(x, y + 10, w, h), 9, 9), paint("#000000", 0.55 * a, blur=16))

    body = skia.Path()
    body.addRRect(rr)
    if m["kind"] == "out":
        px0, py0, px1, py1 = preview_rect(m)
        hole = skia.Path()
        hole.addRRect(skia.RRect.MakeRectXY(skia.Rect.MakeLTRB(px0, py0, px1, py1), 5, 5))
        body = skia.Op(body, hole, skia.PathOp.kDifference_PathOp)
    c.drawPath(body, paint("#2A2D34", a))
    c.save()
    c.clipRRect(rr, True)
    c.drawRect(skia.Rect.MakeXYWH(x, y, w, HEADER), paint(m["col"], a))
    c.restore()
    # a flash round the module as it lands
    flash = math.exp(-pop * 6) * a
    c.drawRRect(rr, paint(m["col"], 0.9 * a, stroke=2.0))
    if flash > 0.01:
        ring = skia.RRect.MakeRectXY(skia.Rect.MakeXYWH(x - 14 * pop, y - 14 * pop, w + 28 * pop, h + 28 * pop), 12, 12)
        c.drawRRect(ring, paint("#FFFFFF", flash, stroke=3))
    text(c, m["name"], x + 12, y + 22, font(17, 650), "#101114", align="l", alpha=a)

    fl = font(13, 500)
    for i, name in enumerate(m["ins"]):
        sx, sy = sock_in(m, i)
        c.drawCircle(sx, sy, 6.5, paint("#141518", a))
        c.drawCircle(sx, sy, 4.8, paint("#8A92A0", a))
        text(c, name, sx + 13, sy + 4.5, fl, "#C8CCD4", align="l", alpha=a)
    for i, name in enumerate(m["outs"]):
        sx, sy = sock_out(m, i)
        c.drawCircle(sx, sy, 6.5, paint("#141518", a))
        c.drawCircle(sx, sy, 4.8, paint(m["col"], a))
        text(c, name, sx - 13, sy + 4.5, fl, "#C8CCD4", align="r", alpha=a)

    # what each module shows inside
    k = m["kind"]
    if k == "time":
        text(c, "%d:%05.2f" % (int(t // 60), t % 60), x + 14, y + 96, font(22, 600, True), "#E9ECF1", align="l", alpha=a)
    elif k == "sine":
        p = skia.Path()
        for j in range(61):
            u = j / 60
            px = x + 70 + u * (w - 100)
            py = y + 92 + math.sin(u * 4 * math.pi - t * 7) * 16
            p.moveTo(px, py) if j == 0 else p.lineTo(px, py)
        c.drawPath(p, paint("#4FC387", a, stroke=2.4, cap="round"))
    elif k == "seq":
        cur = arp_step(t) % 8
        for j in range(8):
            r = skia.Rect.MakeXYWH(x + 14 + j * 24.5, y + h - 36, 19, 19)
            on = j == cur
            c.drawRRect(skia.RRect.MakeRectXY(r, 4, 4), paint("#E0A84A" if on else "#3A3E46", a))
            if on:
                c.drawRRect(skia.RRect.MakeRectXY(r, 4, 4), paint("#FFE2A8", a * 0.9, blur=6))
    elif k == "kal":
        ccx, ccy = x + w - 52, y + h - 42
        for j in range(8):
            ang = t * 1.2 + j * math.pi / 4
            c.drawLine(ccx, ccy, ccx + math.cos(ang) * 28, ccy + math.sin(ang) * 28, paint("#B484E0", a * 0.9, stroke=2, cap="round"))
        c.drawCircle(ccx, ccy, 30, paint("#B484E0", a * 0.5, stroke=1.5))
    elif k == "voice":
        p = skia.Path()
        bx, by, bw, bh = x + 86, y + 58, w - 100, 56
        p.moveTo(bx, by + bh)
        p.lineTo(bx + bw * 0.08, by)
        p.lineTo(bx + bw * 0.35, by + bh * 0.45)
        p.lineTo(bx + bw * 0.75, by + bh * 0.45)
        p.lineTo(bx + bw, by + bh)
        c.drawPath(p, paint("#D87A48", a, stroke=2.2, cap="round"))
        g = (t % (B / 2)) / (B / 2)
        c.drawCircle(bx + bw * g, by + bh * 0.45 if 0.35 < g < 0.75 else by + bh * (0.45 if g >= 0.75 else 0.2), 4, paint("#FFFFFF", a))
    elif k == "fb":
        ccx, ccy = x + w - 50, y + h - 40
        for j in range(6):
            sz = 30 * (0.78 ** j)
            c.save()
            c.translate(ccx, ccy)
            c.rotate(math.degrees(t * 0.8) + j * 9)
            c.drawRect(skia.Rect.MakeXYWH(-sz, -sz, 2 * sz, 2 * sz), paint("#3FC8C8", a * (1 - j * 0.14), stroke=1.6))
            c.restore()
    elif k == "out":
        px0, py0, px1, py1 = preview_rect(m)
        c.drawRRect(skia.RRect.MakeRectXY(skia.Rect.MakeLTRB(px0, py0, px1, py1), 5, 5), paint("#141518", a, stroke=2))
        # a small waveform under the picture
        p = skia.Path()
        seg = music_window(t, 400)
        for j in range(0, 400, 4):
            px = px0 + (px1 - px0) * j / 400
            py = py1 + 22 + seg[j] * 30
            p.moveTo(px, py) if j == 0 else p.lineTo(px, py)
        c.drawPath(p, paint("#E9ECF1", a * 0.85, stroke=1.6))
    c.restore()


_audio = None


def audio():
    global _audio
    if _audio is None:
        _audio = np.load("music.npy")
    return _audio


def music_window(t, n, step=4):
    a = audio()[0]
    i1 = int(t * SR)
    i0 = i1 - n * step
    if i0 < 0:
        return np.zeros(n)
    return a[i0:i1:step] * 2.5


def draw_badge(c, x, y, label, icon, a, t):
    f = font(22, 750)
    tw, _ = measure(label, f, 3)
    w = tw + 96
    h = 58
    rr = skia.RRect.MakeRectXY(skia.Rect.MakeXYWH(x, y - h / 2, w, h), 29, 29)
    c.drawRRect(rr, paint("#1C1E22", a))
    gp = gradient_paint(x, x + w, a)
    gp.setStyle(skia.Paint.kStroke_Style)
    gp.setStrokeWidth(2)
    c.drawRRect(rr, gp)
    ix, iy = x + 34, y
    ip = paint("#E9ECF1", a, stroke=2.4, cap="round")
    if icon == "picture":
        c.drawRRect(skia.RRect.MakeRectXY(skia.Rect.MakeXYWH(ix - 14, iy - 11, 28, 19), 3, 3), ip)
        c.drawLine(ix - 7, iy + 14, ix + 7, iy + 14, ip)
    else:
        sp = skia.Path()
        sp.moveTo(ix - 13, iy - 5); sp.lineTo(ix - 7, iy - 5); sp.lineTo(ix, iy - 11)
        sp.lineTo(ix, iy + 11); sp.lineTo(ix - 7, iy + 5); sp.lineTo(ix - 13, iy + 5); sp.close()
        c.drawPath(sp, ip)
        for r in (6, 11):
            arc = skia.Path()
            arc.addArc(skia.Rect.MakeXYWH(ix + 2 - r, iy - r, 2 * r, 2 * r), -45, 90)
            c.drawPath(arc, ip)
    text(c, label, x + 64, y + 8, f, BRIGHT, align="l", track=3, alpha=a)
    return w


def draw_patch_scene(c, t):
    c.drawColor(hexc("#17191C"))
    z, fcs = camera(t)
    c.save()
    c.translate(W / 2, H / 2)
    c.scale(z, z)
    c.translate(-fcs[0], -fcs[1])
    draw_grid(c, t, z, fcs)

    pops = [pop_amount(t, i) for i in range(len(MODS))]
    # wires, under the modules
    for (si, so, di, dn) in WIRES:
        src, dst = MODS[si], MODS[di]
        t0 = max(MODULE_POPS[si], MODULE_POPS[di]) + 0.12
        prog = ease_out_cubic(lin(t, t0, t0 + 0.38))
        if prog <= 0:
            continue
        path = wire_path(sock_out(src, so), sock_in(dst, dn))
        pm = skia.PathMeasure(path, False)
        L = pm.getLength()
        seg = skia.Path()
        pm.getSegment(0, L * prog, seg, True)
        c.drawPath(seg, paint(src["col"], 0.22, stroke=10, cap="round", blur=4))
        c.drawPath(seg, paint(src["col"], 0.95, stroke=3.2, cap="round"))
        if prog >= 1:
            for j in range(3):
                u = (t * 0.9 + j / 3 + si * 0.13) % 1
                pos, _ = pm.getPosTan(L * u)
                c.drawCircle(pos.x(), pos.y(), 4.2, paint("#FFFFFF", 0.95))
                c.drawCircle(pos.x(), pos.y(), 9, paint(src["col"], 0.5, blur=5))
    for i, m in enumerate(MODS):
        draw_module(c, m, t, pops[i], i)

    # the two things the Output makes
    out = MODS[6]
    t_b = 13.5 * B
    for j, (label, icon, dy) in enumerate([("PICTURE", "picture", -64), ("SOUND", "sound", 64)]):
        tb = t_b + j * 0.12
        a = ease_out_expo(lin(t, tb, tb + 0.4))
        if a <= 0:
            continue
        sx, sy = out["x"] + out["w"], out["y"] + out["h"] / 2
        bx, by = sx + 80, sy + dy
        path = wire_path((sx, sy), (bx, by))
        pm = skia.PathMeasure(path, False)
        L = pm.getLength()
        seg = skia.Path()
        pm.getSegment(0, L * a, seg, True)
        dash = paint("#E9ECF1", 0.7, stroke=2.4, cap="round")
        dash.setPathEffect(skia.DashPathEffect.Make([2, 9], -t * 40))
        c.drawPath(seg, dash)
        c.save()
        c.translate(bx, by)
        sc = 0.7 + 0.3 * ease_out_back(lin(t, tb + 0.1, tb + 0.45))
        c.scale(sc, sc)
        draw_badge(c, 0, 0, label, icon, clamp(a * 1.5), t)
        c.restore()
    c.restore()

    # headline, in screen space
    lead_out = lin(t, S_DROP - B, S_DROP - B * 0.4)
    if t > 12 * B - 0.1:
        f = font(96, 880)
        t_in = 12 * B
        x0, tw = 960 - measure("ONE PATCH.", f, -3)[0] / 2, measure("ONE PATCH.", f, -3)[0]
        anim = rise(t, t_in, None, 96)
        c.save()
        c.translate(960, 150)
        c.scale(1 + lead_out * 0.4, 1 + lead_out * 0.4)
        c.translate(-960, -150)
        masked(c, x0, x0 + tw, 190, 96,
               lambda: text(c, "ONE PATCH.", 960, 190, f, BRIGHT, track=-3, letter=anim, alpha=1 - lead_out, shadow=1))
        f2 = font(56, 720)
        s2 = "A picture and a sound."
        tw2, _ = measure(s2, f2, -1)
        x2 = 960 - tw2 / 2
        anim2 = rise(t, 13 * B, None, 56, stagger=0.014)
        masked(c, x2, x2 + tw2, 262, 56,
               lambda: text(c, s2, 960, 262, f2, track=-1, letter=anim2, grad=(x2, x2 + tw2), alpha=1 - lead_out))
        c.restore()


def patch_preview_rect(t):
    out = MODS[6]
    x0, y0, x1, y1 = preview_rect(out)
    a = pop_amount(t, 6)
    s = 0.55 + 0.45 * ease_out_back(a, 2.2)
    cx, cy = out["x"] + out["w"] / 2, out["y"] + out["h"] / 2
    pts = [(cx + (x - cx) * s, cy + (y - cy) * s) for x, y in ((x0, y0), (x1, y1))]
    (sx0, sy0), (sx1, sy1) = [to_screen(t, *p) for p in pts]
    z, _ = camera(t)
    return (sx0, sy0, sx1, sy1), clamp(a * 4), 5 * z * s


# ---------------------------------------------------------------- scene 3: the drop

def draw_hud(c, t, idx):
    a = 1.0
    mono = font(20, 600, True)
    inset = 46
    L = 34
    pb = paint("#E9ECF1", 0.75, stroke=2.4)
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        x = inset if sx == 0 else W - inset
        y = inset if sy == 0 else H - inset
        dx = L if sx == 0 else -L
        dy = L if sy == 0 else -L
        c.drawLine(x, y, x + dx, y, pb)
        c.drawLine(x, y, x, y + dy, pb)
    rec = (t * 2) % 1 < 0.6
    if rec:
        c.drawCircle(W - inset - 190, inset + 36, 8, paint("#E05A5A", 1.0))
    text(c, "REC", W - inset - 172, inset + 43, mono, "#E9ECF1", align="l", alpha=0.9, track=2)
    frames = int((t % 1) * 60)
    text(c, "00:%02d:%02d" % (int(t), frames), W - inset - 22, inset + 43, mono, "#E9ECF1", align="r", alpha=0.9)
    text(c, "FLYBACK", inset + 22, inset + 43, font(20, 700, True), "#E9ECF1", align="l", alpha=0.9, track=5)
    text(c, "128 BPM  ·  A MINOR", inset + 22, H - inset - 24, mono, "#C8CCD4", align="l", alpha=0.75, track=2)
    text(c, "PRESET %02d / 07" % (idx + 1), W - inset - 22, H - inset - 24, mono, "#C8CCD4", align="r", alpha=0.75, track=2)


def draw_drop_scene(c, t):
    idx = min(6, int((t - S_DROP) / B))
    tb = S_DROP + idx * B
    u = (t - tb) / B
    word = WORDS[idx]
    f = font(178, 900)
    sc = 1.0 + 0.22 * (1 - ease_out_expo(u * 3.2)) + 0.03 * u
    a = clamp(u * 12)
    c.save()
    c.translate(960, 540)
    c.scale(sc, sc)
    c.translate(-960, -540)
    total, _ = measure(word, f, -4)
    c.drawRRect(skia.RRect.MakeRectXY(skia.Rect.MakeXYWH(960 - total / 2 - 40, 540 - 150, total + 80, 200), 30, 30),
                paint("#000000", 0.6 * a, blur=70))
    text(c, word, 960, 604, f, "#FFFFFF", track=-4, alpha=a)
    c.restore()
    text(c, "%02d" % (idx + 1), 960, 400, font(24, 600, True), "#E9ECF1", alpha=a * 0.85, track=4)
    draw_hud(c, t, idx)


# ---------------------------------------------------------------- scene 4-5: the mark and the lockup

MARK_RAMP1 = ((73, 92), (183, 56))
MARK_STEM = ((73, 92), (73, 200))
MARK_RAMP2 = ((73, 148), (165, 118))
MARK_DOT = (165, 118)


def lockup_geometry():
    fw = font(176, 760)
    ww, _ = measure("Flyback", fw, -4)
    s = 1.3
    gap = 40
    mark_w = (193.5 - 62.5) * s
    total = mark_w + gap + ww
    left = 960 - total / 2
    mx = left - 62.5 * s               # mark origin
    cy = 450
    my = cy - 128 * s
    cap = fw.getMetrics().fCapHeight
    base = cy + cap / 2 + 4
    wx = left + mark_w + gap
    return dict(s=s, mx=mx, my=my, wx=wx, base=base, ww=ww, fw=fw)


def mark_transform(t):
    """Scale and origin of the mark: big and centered while it is drawn, then moving into the lockup."""
    g = lockup_geometry()
    s0 = 2.4
    x0 = 960 - 128 * s0
    y0 = 520 - 128 * s0
    u = ease_out_expo(lin(t, S_LOGO + 0.02, S_LOGO + 0.62))
    s = s0 + (g["s"] - s0) * u
    x = x0 + (g["mx"] - x0) * u
    y = y0 + (g["my"] - y0) * u
    return s, x, y


def mark_draw_progress(t):
    return lin(t, S_LOGO_DRAW + 0.03, S_LOGO - 0.06)


def mark_head(t):
    """Where the beam is on the mark while it is drawn, in 256-space."""
    p = mark_draw_progress(t)
    if p < 0.4:
        u = p / 0.4
        (x0, y0), (x1, y1) = MARK_RAMP1
    elif p < 0.58:
        u = (p - 0.4) / 0.18
        (x0, y0), (x1, y1) = MARK_STEM
    else:
        u = (p - 0.58) / 0.42
        (x0, y0), (x1, y1) = MARK_RAMP2
    return x0 + (x1 - x0) * u, y0 + (y1 - y0) * u


def draw_mark(c, t, full=False):
    s, ox, oy = mark_transform(t)
    p = 1.0 if full else mark_draw_progress(t)
    c.save()
    c.translate(ox, oy)
    c.scale(s, s)
    red = paint("#E05A5A", 1.0, stroke=21, cap="round")
    red_glow = paint("#E05A5A", 0.55, stroke=21, cap="round", blur=9)

    def seg(a, b, u):
        return a, (a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u)

    stem_u = clamp((p - 0.4) / 0.18)
    r1_u = clamp(p / 0.4)
    r2_u = clamp((p - 0.58) / 0.42)
    if stem_u > 0:
        a, b = seg(*MARK_STEM, stem_u)
        c.drawLine(*a, *b, red_glow)
        c.drawLine(*a, *b, red)
    g = skia.Paint(AntiAlias=True, StrokeWidth=21, Style=skia.Paint.kStroke_Style, StrokeCap=skia.Paint.kRound_Cap)
    g.setShader(skia.GradientShader.MakeLinear([(73, 92), (183, 56)], SWEEP))
    if r1_u > 0:
        a, b = seg(*MARK_RAMP1, r1_u)
        c.drawLine(*a, *b, g)
    if r2_u > 0:
        a, b = seg(*MARK_RAMP2, r2_u)
        c.drawLine(*a, *b, g)
    if p >= 1:
        dx, dy = MARK_DOT
        c.drawCircle(dx, dy, 20, paint("#E0A84A", 0.3, blur=9))
        c.drawCircle(dx, dy, 13, paint("#E0A84A"))
        c.drawCircle(dx, dy, 5.5, paint("#FFF3DC"))
    c.restore()


def dot_screen(t):
    s, ox, oy = mark_transform(t)
    return ox + MARK_DOT[0] * s, oy + MARK_DOT[1] * s


SPARKS = None


def sparks():
    global SPARKS
    if SPARKS is None:
        r = np.random.default_rng(3)
        SPARKS = [(r.uniform(0, 2 * math.pi), r.uniform(300, 1500), r.uniform(0.35, 1.1), r.choice(["#FFF3DC", "#E0A84A", "#3FC8C8", "#E9ECF1"]))
                  for _ in range(90)]
    return SPARKS


def draw_sparks(c, t):
    if not (S_LOGO <= t < S_LOGO + 1.2):
        return
    tau = t - S_LOGO
    ox, oy = dot_screen(S_LOGO)
    for ang, speed, life, col in sparks():
        if tau > life:
            continue
        k = tau / life
        d = speed * (1 - math.exp(-tau * 3.2)) / 3.2
        x = ox + math.cos(ang) * d
        y = oy + math.sin(ang) * d + 120 * tau * tau
        vx = math.cos(ang) * speed * math.exp(-tau * 3.2)
        vy = math.sin(ang) * speed * math.exp(-tau * 3.2) + 240 * tau
        tail = 0.03
        p = paint(col, (1 - k) ** 1.5, stroke=2.6 * (1 - k) + 0.6, cap="round")
        c.drawLine(x, y, x - vx * tail, y - vy * tail, p)


def draw_saw_strip(c, t, a):
    if a <= 0:
        return
    y = 1004
    period = 26
    reveal = ease_out_expo(lin(t, S_LOGO + 0.1, S_LOGO + 1.0))
    p = skia.Path()
    off = (t * 60) % period
    x = -period + off
    p.moveTo(x, y + 9)
    while x < W + period:
        p.lineTo(x + period, y)
        p.lineTo(x + period, y + 9)
        x += period
    gp = gradient_paint(0, W, a * 0.75)
    gp.setStyle(skia.Paint.kStroke_Style)
    gp.setStrokeWidth(1.8)
    c.save()
    c.clipRect(skia.Rect.MakeLTRB(W / 2 - W / 2 * reveal, 0, W / 2 + W / 2 * reveal, H))
    c.drawPath(p, gp)
    c.restore()


def draw_logo_scene(c, t):
    if t < S_LOGO:
        draw_mark(c, t)
        return
    g = lockup_geometry()
    draw_saw_strip(c, t, 1.0)
    # wordmark slides out from behind the mark
    u = ease_out_expo(lin(t, S_LOGO + 0.12, S_LOGO + 0.85))
    if u > 0:
        s, ox, oy = mark_transform(t)
        clip_x = ox + 193.5 * s + 6
        c.save()
        c.clipRect(skia.Rect.MakeLTRB(clip_x, 0, W, H))
        text(c, "Flyback", g["wx"] - (1 - u) * (g["ww"] * 0.55), g["base"], g["fw"], BRIGHT, align="l", track=-4, alpha=clamp(u * 2))
        c.restore()
    draw_mark(c, t, full=True)
    draw_sparks(c, t)
    # shock ring drawn, on top of the distortion the tube adds
    tau = t - S_LOGO
    if tau < 0.7:
        ox, oy = dot_screen(S_LOGO)
        r = 40 + 1400 * ease_out_cubic(tau / 0.7)
        c.drawCircle(ox, oy, r, paint("#FFF3DC", 0.8 * (1 - tau / 0.7) ** 2, stroke=6 * (1 - tau / 0.7) + 1))

    # tagline
    f = font(52, 640)
    pre, key = "One patch. ", "A picture and a sound."
    tot, _ = measure(pre + key, f, -1)
    wp, _ = measure(pre, f, -1)
    x0 = 960 - tot / 2
    y = 640
    anim = rise(t, S_LOGO + 1.5 * B, None, 52, stagger=0.012)
    masked(c, x0, x0 + tot, y, 52, lambda: (
        text(c, pre, x0, y, f, "#C8CCD4", align="l", track=-1, letter=anim),
        text(c, key, x0 + wp - 1, y, f, align="l", track=-1, letter=lambda i: anim(i + len(pre)), grad=(x0 + wp, x0 + tot))))

    # info row
    info = "FREE  ·  OPEN SOURCE  ·  WINDOWS  ·  MACOS  ·  LINUX"
    fi = font(22, 560, True)
    ai = ease_out_expo(lin(t, S_LOGO + 3 * B, S_LOGO + 3 * B + 0.6))
    text(c, info, 960, 730 + (1 - ai) * 16, fi, "#8A909A", track=3, alpha=ai)

    # the address, in a pill
    ap = lin(t, S_LOGO + 4 * B, S_LOGO + 4 * B + 0.5)
    if ap > 0:
        fu = font(28, 620, True)
        url = "markovcd.github.io/Flyback"
        tw, _ = measure(url, fu, 0)
        pw, ph = tw + 110, 70
        sc = 0.8 + 0.2 * ease_out_back(ap, 2.4)
        c.save()
        c.translate(960, 830)
        c.scale(sc, sc)
        rr = skia.RRect.MakeRectXY(skia.Rect.MakeXYWH(-pw / 2, -ph / 2, pw, ph), 35, 35)
        c.drawRRect(rr, paint("#1C1E22", clamp(ap * 3) * 0.9))
        gp = gradient_paint(-pw / 2, pw / 2, clamp(ap * 3))
        gp.setStyle(skia.Paint.kStroke_Style)
        gp.setStrokeWidth(2.4)
        c.drawRRect(rr, gp)
        # download arrow
        ax = -pw / 2 + 42
        ip = paint("#E9ECF1", clamp(ap * 3), stroke=2.8, cap="round")
        c.drawLine(ax, -12, ax, 6, ip)
        c.drawLine(ax - 8, -2, ax, 6, ip)
        c.drawLine(ax + 8, -2, ax, 6, ip)
        c.drawLine(ax - 10, 13, ax + 10, 13, ip)
        text(c, url, -pw / 2 + 72, 10, fu, BRIGHT, align="l", alpha=clamp(ap * 3))
        c.restore()


def draw(c, t):
    c.clear(skia.Color(0, 0, 0, 0))
    if t < S_PATCH:
        draw_beam_scene(c, t)
    elif t < S_DROP:
        draw_patch_scene(c, t)
    elif t < S_LOGO_DRAW:
        draw_drop_scene(c, t)
    else:
        draw_logo_scene(c, t)
