"""GLSL for the picture: presets, the feedback pass, the composite, bloom and the tube."""

VERT = """
#version 330
in vec2 pos;
out vec2 uv;
void main() { uv = pos * 0.5 + 0.5; gl_Position = vec4(pos, 0.0, 1.0); }
"""

COMMON = """
#version 330
in vec2 uv;
out vec4 fragColor;
uniform vec2 res;
const float PI = 3.14159265;
mat2 rot(float a) { float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }
vec3 lin(vec3 c) { return pow(c, vec3(2.2)); }
float hash21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float hash31(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float vnoise(vec3 x) {
    vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash31(i), hash31(i + vec3(1,0,0)), f.x),
                   mix(hash31(i + vec3(0,1,0)), hash31(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash31(i + vec3(0,0,1)), hash31(i + vec3(1,0,1)), f.x),
                   mix(hash31(i + vec3(0,1,1)), hash31(i + vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm(vec3 p) { float a = 0.5, s = 0.0; for (int i = 0; i < 5; i++) { s += a * vnoise(p); p = p * 2.03 + 11.7; a *= 0.5; } return s; }
vec3 hsv2rgb(vec3 c) { vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0); return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y); }

// brand, linear
const vec3 C_SOURCE = vec3(0.069, 0.341, 0.730);
const vec3 C_FEED   = vec3(0.054, 0.578, 0.578);
const vec3 C_OSC    = vec3(0.082, 0.545, 0.238);
const vec3 C_PATTERN= vec3(0.745, 0.392, 0.069);
const vec3 C_SINK   = vec3(0.745, 0.107, 0.107);
const vec3 C_SPACE  = vec3(0.458, 0.225, 0.745);
const vec3 C_TINT   = vec3(0.745, 0.144, 0.479);
const vec3 C_BEAM   = vec3(1.0, 0.90, 0.74);

vec3 sweep(float x) {   // blue -> cyan -> green
    return x < 0.5 ? mix(C_SOURCE, C_FEED, x * 2.0) : mix(C_FEED, C_OSC, x * 2.0 - 1.0);
}

uniform float seqLit[16];
uniform float seqRow[16];
uniform vec4 notes[32];
uniform int nNotes;
uniform sampler2D audioTex;   // 2048 x 2, r32f, the window ending now

// ---- presets: p is centered, y spans -0.5..0.5 ----
vec3 preset0(vec2 p, float t, float k) {       // OSCILLATORS: plasma
    p *= 1.0 - 0.12 * k;
    float f = sin(p.x * 5.0 + t * 1.3) + sin(p.y * 6.5 - t * 1.1)
            + sin(length(p - vec2(0.2 * sin(t * 0.7), 0.0)) * 11.0 - t * 2.4) + sin((p.x + p.y) * 4.0 + t * 0.7);
    vec3 c = mix(sweep(fract(f * 0.11 + t * 0.06)), C_TINT, smoothstep(-0.2, 1.0, sin(f * 0.9 + t * 0.5)));
    c *= (0.35 + 0.65 * pow(0.5 + 0.5 * sin(f * 2.2), 2.0)) * (0.9 + 0.7 * k);
    float lines = pow(0.5 + 0.5 * cos(f * 9.0), 24.0);
    return c + lines * vec3(0.9, 0.95, 1.0) * 0.3;
}

vec3 preset1(vec2 p, float t, float k) {       // FEEDBACK: tunnel
    p = rot(t * 0.4) * p;
    float r = length(p), a = atan(p.y, p.x);
    float z = 0.28 / (r + 0.015) + t * 2.6;
    float rings = pow(0.5 + 0.5 * cos(z * 6.28318), 10.0);
    float seg = pow(0.5 + 0.5 * cos(a * 6.0 + z * 1.2), 3.0);
    vec3 c = mix(C_SOURCE, C_FEED * 1.2, seg) * (0.08 + 1.1 * rings);
    c += C_BEAM * pow(rings, 4.0) * seg * 0.7;
    float fog = smoothstep(0.02, 0.45, r);
    return c * fog * (0.7 + 0.8 * k);
}

vec3 preset2(vec2 p, float t, float k) {       // GEOMETRY: kaleidoscope
    p = rot(t * 0.35) * p;
    p *= 1.0 - 0.15 * k;
    float r = length(p), a = atan(p.y, p.x);
    float seg = 6.28318 / 8.0;
    a = mod(a, seg); a = abs(a - seg * 0.5);
    vec2 q = r * vec2(cos(a), sin(a));
    float n = fbm(vec3(q * 2.6, t * 0.45));
    vec2 w = q + 0.32 * vec2(n, fbm(vec3(q * 2.6 + 5.2, t * 0.45)));
    float bands = sin(length(w) * 16.0 - t * 3.2 + n * 5.0);
    float v = smoothstep(-0.15, 0.85, bands);
    vec3 c = lin(hsv2rgb(vec3(mix(0.86, 1.09, n), 0.82, 1.0)));
    return c * v * (0.55 + 0.6 * k) + C_BEAM * pow(max(bands, 0.0), 30.0) * 0.5;
}

vec3 preset3(vec2 p, float t, float k) {       // PATTERNS: log-polar checker
    float r = length(p), a = atan(p.y, p.x);
    float u = log(r + 1e-3) * 2.6 - t * 1.9;
    float v = a / PI * 6.0 + t * 0.25 + u * 0.35;
    vec2 g = fract(vec2(u, v)) - 0.5;
    float ch = mod(floor(u) + floor(v), 2.0);
    float e = min(abs(g.x), abs(g.y));
    float glow = exp(-e * 22.0);
    vec3 c = mix(C_SPACE * 0.06, C_OSC * 0.5, ch) + glow * mix(C_SPACE, vec3(1.0), 0.3) * 0.7;
    return c * smoothstep(0.0, 0.3, r) * (0.75 + 0.6 * k);
}

vec3 preset4(vec2 pix, float t, float k) {     // SEQUENCERS: step grid lit by the arp
    float cell = 92.0;
    vec2 org = vec2(res.x * 0.5 - cell * 8.0, res.y * 0.5 - cell * 4.0);
    vec2 g = (pix - org) / cell;
    vec3 c = vec3(0.004);
    if (g.x >= 0.0 && g.x < 16.0 && g.y >= 0.0 && g.y < 8.0) {
        int col = int(floor(g.x));
        float row = floor(g.y);
        vec2 f = fract(g) - 0.5;
        float d = length(max(abs(f) - 0.3, 0.0)) - 0.08;
        float box = smoothstep(0.02, -0.02, d);
        float lit = seqLit[col] * float(abs(row - seqRow[col]) < 0.5);
        float head = seqLit[col];
        vec3 base = mix(vec3(0.018, 0.02, 0.026), C_FEED * 0.12, head * 0.8);
        vec3 on = mix(C_PATTERN, C_BEAM, 0.35) * 3.2;
        c = mix(c, base + on * lit, box);
        c += on * lit * exp(-max(d, 0.0) * 9.0) * 0.5;
    }
    return c * (1.0 + 0.4 * k);
}

vec3 preset5(vec2 p, float t, float k) {       // SCOPES: the sound drawn round a circle
    float r = length(p), a = atan(p.y, p.x);
    float u = fract((a + PI) / (2.0 * PI) + t * 0.05);
    float sL = texture(audioTex, vec2(u, 0.25)).r;
    float sR = texture(audioTex, vec2(fract(u + 0.5), 0.75)).r;
    float r1 = 0.24 + 0.16 * sL * (1.0 + k);
    float r2 = 0.34 + 0.10 * sR;
    float l1 = exp(-abs(r - r1) * 260.0) * 2.4 + exp(-abs(r - r1) * 40.0) * 0.35;
    float l2 = exp(-abs(r - r2) * 320.0) * 1.3;
    vec3 c = sweep(u) * 1.5 * l1 + C_TINT * l2 * 1.6;
    c += C_FEED * 0.05 * smoothstep(0.5, 0.0, r);
    return c;
}

float keyIsBlack(int m) { int n = m % 12; return float(n == 1 || n == 3 || n == 6 || n == 8 || n == 10); }

vec3 preset6(vec2 pix, float t, float k) {     // MIDI: a piano roll falling onto keys
    float lo = 57.0, hi = 94.0;
    float kw = res.x / (hi - lo);
    float m = floor(pix.x / kw) + lo;
    float keyTop = res.y * 0.80;
    vec3 c = vec3(0.004);
    float x = fract(pix.x / kw);
    // falling notes
    for (int i = 0; i < 32; i++) {
        if (i >= nNotes) break;
        vec4 nt = notes[i];                         // midi, start - now, duration, lead?
        if (abs(nt.x - m) > 0.5) continue;
        float y0 = keyTop - nt.y * 900.0;           // start edge reaches the keys at its time
        float y1 = y0 - nt.z * 900.0;
        float inside = step(y1, pix.y) * step(pix.y, y0) * smoothstep(0.0, 0.08, x) * smoothstep(1.0, 0.92, x);
        vec3 col = nt.w > 0.5 ? C_TINT * 2.4 : sweep((nt.x - lo) / (hi - lo)) * 2.0;
        c += col * inside * step(pix.y, keyTop);
    }
    // keys
    if (pix.y > keyTop) {
        float black = keyIsBlack(int(m));
        float pressed = 0.0;
        vec3 pc = vec3(0.0);
        for (int i = 0; i < 32; i++) {
            if (i >= nNotes) break;
            vec4 nt = notes[i];
            if (abs(nt.x - m) < 0.5 && nt.y <= 0.0 && nt.y + nt.z > 0.0) { pressed = 1.0; pc = nt.w > 0.5 ? C_TINT : sweep((nt.x - lo) / (hi - lo)); }
        }
        vec3 kc = black > 0.5 ? vec3(0.006) : vec3(0.16);
        float edge = smoothstep(0.0, 0.04, x) * smoothstep(1.0, 0.96, x);
        c = mix(vec3(0.0), kc, edge) * smoothstep(res.y, res.y - 20.0, pix.y) + pc * pressed * 2.5 * edge;
    }
    c += C_FEED * exp(-abs(pix.y - keyTop) * 0.25) * 0.5;
    return c * (1.0 + 0.3 * k);
}

vec3 preset(int i, vec2 pix, float t, float k) {
    vec2 p = (pix - res * 0.5) / res.y;
    if (i == 0) return preset0(p, t, k);
    if (i == 1) return preset1(p, t, k);
    if (i == 2) return preset2(p, t, k);
    if (i == 3) return preset3(p, t, k);
    if (i == 4) return preset4(pix, t, k);
    if (i == 5) return preset5(p, t, k);
    return preset6(pix, t, k);
}
"""

# Feedback pass: the beam's phosphor, and the drop's trails.
FEEDBACK = COMMON + """
uniform sampler2D prev;
uniform float t, kick, decay, fbZoom, fbRot;
uniform int mode;          // 0 beam, 1 preset
uniform int presetId;
uniform vec4 beam[96];     // x, y (pixels from top-left), unused, intensity
uniform vec3 beamCol[96];
uniform int nBeam;
void main() {
    vec2 pix = vec2(uv.x, 1.0 - uv.y) * res;
    vec2 c = uv - 0.5; c.x *= res.x / res.y;
    c = rot(fbRot) * c / fbZoom; c.x /= res.x / res.y;
    vec3 old = texture(prev, c + 0.5).rgb * decay;
    vec3 col;
    if (mode == 0) {
        vec3 add = vec3(0.0);
        for (int i = 0; i < 96; i++) {
            if (i >= nBeam) break;
            vec2 d = pix - beam[i].xy;
            float d2 = dot(d, d);
            add += beamCol[i] * beam[i].w * (exp(-d2 / 32.0) + 0.18 * exp(-d2 / 500.0));
        }
        col = old + add;
    } else {
        vec3 fresh = preset(presetId, pix, t, kick);
        col = max(fresh, old);
    }
    fragColor = vec4(col, 1.0);
}
"""

COMPOSITE = COMMON + """
uniform sampler2D fbTex, uiTex;
uniform float t, kick, fbAmt, scopeAmt, uiGain, bgMode;
uniform vec4 prevRect;      // x0, y0, x1, y1 in pixels from top-left
uniform float prevAlpha, prevRadius;
uniform vec3 head;          // beam head x, y, intensity
uniform float glowAmt;
float rbox(vec2 p, vec2 b, float r) { vec2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }
void main() {
    vec2 pix = vec2(uv.x, 1.0 - uv.y) * res;
    vec2 p = (pix - res * 0.5) / res.y;
    vec3 col = vec3(0.0);
    // backgrounds
    if (bgMode > 0.5) {
        vec3 base = vec3(0.0035, 0.004, 0.005);
        float g1 = exp(-dot(p - vec2(-0.35, -0.05), p - vec2(-0.35, -0.05)) * 5.0);
        float g2 = exp(-dot(p - vec2(0.45, 0.1), p - vec2(0.45, 0.1)) * 6.0);
        float breathe = 0.8 + 0.2 * sin(t * 2.0);
        col = base + (C_FEED * g1 * 0.035 + C_SINK * g2 * 0.02) * glowAmt * breathe * (1.0 + 0.8 * kick);
        float n = fbm(vec3(p * 3.0, t * 0.2));
        col += sweep(n) * pow(n, 4.0) * 0.025 * glowAmt;
    }
    col += texture(fbTex, uv).rgb * fbAmt;
    // linear scope across the lower third
    if (scopeAmt > 0.0) {
        float x = pix.x / res.x;
        float s0 = texture(audioTex, vec2(x, 0.25)).r;
        float s1 = texture(audioTex, vec2(x + 1.0 / res.x, 0.25)).r;
        float amp = 150.0;
        float yc = res.y * 0.83;
        float ys = yc - s0 * amp;
        float slope = (s1 - s0) * amp;
        float d = abs(pix.y - ys) / sqrt(1.0 + slope * slope);
        float line = exp(-d * d / 3.0) * 1.6 + exp(-d / 14.0) * 0.12;
        col += mix(vec3(1.0), sweep(x), 0.55) * line * scopeAmt;
    }
    // beam head
    if (head.z > 0.0) {
        vec2 d = pix - head.xy;
        float d2 = dot(d, d);
        col += C_BEAM * head.z * (exp(-d2 / 40.0) * 3.0 + exp(-d2 / 900.0) * 0.6 + exp(-d2 / 12000.0) * 0.12);
    }
    vec4 u = texture(uiTex, vec2(uv.x, 1.0 - uv.y)).zyxw;
    col = mix(col, lin(u.rgb) * uiGain, u.a);
    // the Output module's preview, which the camera flies into
    if (prevAlpha > 0.0) {
        vec2 c = (prevRect.xy + prevRect.zw) * 0.5, h = (prevRect.zw - prevRect.xy) * 0.5;
        float d = rbox(pix - c, h, prevRadius);
        float m = smoothstep(1.0, -1.0, d) * prevAlpha;
        vec2 local = (pix - prevRect.xy) / (prevRect.zw - prevRect.xy) * res;
        col = mix(col, preset(0, local, t, kick), m);
    }
    fragColor = vec4(col, 1.0);
}
"""

PREFILTER = COMMON + """
uniform sampler2D src;
uniform float threshold;
void main() {
    vec3 c = texture(src, uv).rgb;
    float br = max(c.r, max(c.g, c.b));
    float knee = threshold * 0.6;
    float soft = clamp(br - threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 1e-4);
    float contrib = max(soft, br - threshold) / max(br, 1e-4);
    fragColor = vec4(c * contrib, 1.0);
}
"""

DOWN = COMMON + """
uniform sampler2D src;
uniform vec2 texel;
void main() {
    vec3 a = texture(src, uv + texel * vec2(-1, -1)).rgb, b = texture(src, uv + texel * vec2(1, -1)).rgb;
    vec3 c = texture(src, uv + texel * vec2(-1, 1)).rgb, d = texture(src, uv + texel * vec2(1, 1)).rgb;
    vec3 e = texture(src, uv).rgb;
    vec3 f = texture(src, uv + texel * vec2(-2, 0)).rgb, g = texture(src, uv + texel * vec2(2, 0)).rgb;
    vec3 h = texture(src, uv + texel * vec2(0, -2)).rgb, i = texture(src, uv + texel * vec2(0, 2)).rgb;
    fragColor = vec4(e * 0.2 + (a + b + c + d) * 0.125 + (f + g + h + i) * 0.075, 1.0);
}
"""

UP = COMMON + """
uniform sampler2D src, base;
uniform vec2 texel;
uniform float spread;
void main() {
    vec2 o = texel * spread;
    vec3 s = texture(src, uv + vec2(-o.x, -o.y)).rgb + 2.0 * texture(src, uv + vec2(0, -o.y)).rgb + texture(src, uv + vec2(o.x, -o.y)).rgb
           + 2.0 * texture(src, uv + vec2(-o.x, 0)).rgb + 4.0 * texture(src, uv).rgb + 2.0 * texture(src, uv + vec2(o.x, 0)).rgb
           + texture(src, uv + vec2(-o.x, o.y)).rgb + 2.0 * texture(src, uv + vec2(0, o.y)).rgb + texture(src, uv + vec2(o.x, o.y)).rgb;
    fragColor = vec4(texture(base, uv).rgb + s / 16.0, 1.0);
}
"""

FINAL = COMMON + """
uniform sampler2D scene, bloom;
uniform float t, bloomAmt, ca, flash, scan, grain, shake, vignette;
uniform vec2 tube;          // vertical and horizontal opening of the picture, 1 = full
uniform float tubeGlow;
uniform vec4 shock;         // x, y (pixels, top-left), radius, strength
vec3 tonemap(vec3 x) {
    float a = 0.72;
    vec3 over = a + (1.0 - a) * (1.0 - exp(-(x - a) / (1.0 - a)));
    return mix(x, over, step(a, x));
}
vec3 fetch(vec2 q) {
    vec3 c = texture(scene, q).rgb + texture(bloom, q).rgb * bloomAmt;
    return c;
}
void main() {
    vec2 q = uv;
    // the tube opening and closing
    vec2 cq = q - 0.5;
    cq /= max(tube, vec2(1e-4));
    q = cq + 0.5;
    float inside = step(abs(cq.x), 0.5) * step(abs(cq.y), 0.5);
    // shake
    q += shake * vec2(sin(t * 91.0), cos(t * 77.0)) * 0.004;
    // shockwave
    if (shock.w > 0.0) {
        vec2 pix = vec2(q.x, 1.0 - q.y) * res;
        vec2 d = pix - shock.xy;
        float r = length(d);
        float band = exp(-pow((r - shock.z) / 40.0, 2.0));
        vec2 off = normalize(d + 1e-4) * band * shock.w * 28.0;
        q -= vec2(off.x, -off.y) / res;
    }
    // slight barrel
    vec2 b = q - 0.5;
    q = 0.5 + b * (1.0 + 0.025 * dot(b, b));
    vec2 dir = (q - 0.5);
    vec3 col;
    col.r = fetch(q + dir * ca).r;
    col.g = fetch(q).g;
    col.b = fetch(q - dir * ca).b;
    col += flash;
    col = tonemap(col) * inside;
    // a squashed tube is a bright line
    col += vec3(0.9, 0.95, 1.0) * tubeGlow * inside;
    // scanlines, vignette, grain
    float sl = 0.5 + 0.5 * cos(uv.y * res.y * PI * 0.5);
    col *= 1.0 - scan * sl;
    float v = smoothstep(1.25, 0.35, length((uv - 0.5) * vec2(res.x / res.y, 1.0)));
    col *= mix(1.0, v, vignette);
    col = pow(max(col, 0.0), vec3(1.0 / 2.2));
    col += (hash21(uv * res + fract(t * 13.1) * 100.0) - 0.5) * grain;
    fragColor = vec4(col, 1.0);
}
"""
