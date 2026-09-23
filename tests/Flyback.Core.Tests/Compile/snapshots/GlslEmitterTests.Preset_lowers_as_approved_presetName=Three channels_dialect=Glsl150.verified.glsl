#version 150

uniform float uTime;
uniform float uTimeLo;
uniform float uOne;
uniform float uAspect;
uniform float uK[13];

in vec2 vUv;
out vec4 fragColor;

// The exact zero tests below are the point, not an oversight: they trap the
// one divisor that makes a result undefined. See the same reasoning spelled
// out over Divide in CompiledPatch.
const float BIG = 3.402823e38;
const float JUST_BELOW_ONE = 0.99999994;

bool  fin(float v)          { return v == v && abs(v) < BIG; }
float gd (float v)          { return fin(v) ? v : 0.0; }
float fr (float v)          { float f = v - floor(v); return f < 1.0 ? f : JUST_BELOW_ONE; }
float dv (float a, float b) { return b == 0.0 ? 0.0 : gd(a / b); }
float md (float a, float b) { return b == 0.0 ? 0.0 : gd(a - b * floor(a / b)); }
float sq (float a)          { return a <= 0.0 ? 0.0 : sqrt(a); }
float lg (float a)          { return a <= 0.0 ? 0.0 : log(a); }
float sat(float v)          { return fin(v) ? clamp(v, 0.0, 1.0) : 0.0; }

// GLSL leaves sign undefined at a NaN, where the interpreter answers zero.
// Written out rather than guarding the input, which would read an infinity
// as nought and disagree about its sign.
float sg (float v)          { return v > 0.0 ? 1.0 : v < 0.0 ? -1.0 : 0.0; }

// GLSL leaves atan undefined at the origin, where Math.Atan2 answers zero.
float at2(float y, float x) { return (x == 0.0 && y == 0.0) ? 0.0 : atan(y, x); }

// GLSL leaves pow undefined for a negative base, where Math.Pow(-2, 3) is -8.
// The cases that are NaN or infinite on the CPU are the ones Guard turns to
// zero, so they are answered directly here.
float pw(float a, float b)
{
    if (a > 0.0)       return gd(pow(a, b));
    if (a == 0.0)      return b == 0.0 ? 1.0 : 0.0;
    if (b != floor(b)) return 0.0;

    float m = gd(pow(-a, b));
    return mod(abs(b), 2.0) == 1.0 ? -m : m;
}

// GLSL's smoothstep divides by zero when the edges meet; the interpreter
// answers a step there.
float sm(float e0, float e1, float x)
{
    if (e0 == e1) return x < e0 ? 0.0 : 1.0;

    float t = clamp((x - e0) / (e1 - e0), 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

// Noise, transcribed from Noise.cs. Converting a negative int to uint keeps
// the bit pattern in both languages, so the hash agrees exactly, which is
// what stops a noisy patch looking like a different patch on the GPU.
float hsh(int x, int y, int z)
{
    uint h = uint(x) * 374761393u + uint(y) * 668265263u + uint(z) * 1274126177u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return float(h & 0xFFFFFFu) * (1.0 / 16777215.0);
}

float fade(float t) { return t * t * (3.0 - 2.0 * t); }
float lrp (float a, float b, float t) { return a + (b - a) * t; }

float nz(float x, float y, float z)
{
    if (!(fin(x) && fin(y) && fin(z))) return 0.0;

    int xi = int(floor(x)), yi = int(floor(y)), zi = int(floor(z));
    float u = fade(x - float(xi)), v = fade(y - float(yi)), w = fade(z - float(zi));

    float z0 = lrp(lrp(hsh(xi, yi,     zi), hsh(xi + 1, yi,     zi), u),
                   lrp(hsh(xi, yi + 1, zi), hsh(xi + 1, yi + 1, zi), u), v);
    float z1 = lrp(lrp(hsh(xi, yi,     zi + 1), hsh(xi + 1, yi,     zi + 1), u),
                   lrp(hsh(xi, yi + 1, zi + 1), hsh(xi + 1, yi + 1, zi + 1), u), v);

    return lrp(z0, z1, w);
}

// Two floats standing for one number, hi + lo, for whatever the clock feeds:
// a float alone steps by a quarter of a millisecond an hour in and by two
// seconds a year in. Multiplying by uOne, which is 1, stops a compiler folding
// (a + b) - a into b, which is the whole of what these helpers compute.
vec2 qts(float a, float b) { float s = (a + b) * uOne; return vec2(s, b - (s - a) * uOne); }

vec2 tws(float a, float b)
{
    float s = (a + b) * uOne;
    float v = (s - a) * uOne;
    return vec2(s, (a - (s - v)) * uOne + (b - v));
}

vec2 spl(float a)
{
    float t = a * 4097.0;
    float h = t * uOne - (t - a);
    return vec2(h, a * uOne - h);
}

vec2 twp(float a, float b)
{
    float p = a * b;
    vec2 x = spl(a), y = spl(b);
    return vec2(p, ((x.x * y.x - p) + x.x * y.y + x.y * y.x) + x.y * y.y);
}

vec2 dad(vec2 a, vec2 b) { vec2 s = tws(a.x, b.x); return qts(s.x, s.y + a.y + b.y); }
vec2 dml(vec2 a, vec2 b) { vec2 p = twp(a.x, b.x); return qts(p.x, p.y + a.x * b.y + a.y * b.x); }

vec2 ddv(vec2 a, vec2 b)
{
    if (b.x == 0.0) return vec2(0.0);

    float q = a.x / b.x;
    if (!fin(q)) return vec2(0.0);

    vec2 r = dad(a, -dml(vec2(q, 0.0), b));
    return tws(q, (r.x + r.y) / b.x);
}

vec2 dfl(vec2 a) { float h = floor(a.x); return tws(h, floor((a.x - h) + a.y)); }
float dfr(vec2 a) { float h = a.x - floor(a.x); return fr(h + a.y); }

float dmd(vec2 a, vec2 b)
{
    if (b.x == 0.0) return 0.0;

    vec2 r = dad(a, -dml(dfl(ddv(a, b)), b));
    return gd(r.x + r.y);
}

// Whole turns taken off before the one float a sine needs, against 2π in two
// floats of its own.
float dtr(vec2 a)
{
    float k = floor((a.x + a.y) * 0.15915494);
    vec2 r = dad(a, -dml(vec2(k, 0.0), vec2(6.2831855, -1.7484555e-7)));
    return r.x + r.y;
}

// A whole number wrapped to 32 bits the way Noise.Lattice wraps it, in steps
// a float holds exactly, since converting one past an int is undefined.
uint wrp(float v)
{
    float q = floor(v / 65536.0);
    float m = q - 65536.0 * floor(q / 65536.0);
    return (uint(m) << 16) + uint(v - q * 65536.0);
}

int lat(vec2 n) { return int(wrp(n.x) + wrp(n.y)); }

float nzw(vec2 x, vec2 y, vec2 z)
{
    if (!(fin(x.x) && fin(y.x) && fin(z.x))) return 0.0;

    vec2 xf = dfl(x), yf = dfl(y), zf = dfl(z);
    int xi = lat(xf), yi = lat(yf), zi = lat(zf);

    vec2 fx = dad(x, -xf), fy = dad(y, -yf), fz = dad(z, -zf);
    float u = fade(fx.x + fx.y), v = fade(fy.x + fy.y), w = fade(fz.x + fz.y);

    float z0 = lrp(lrp(hsh(xi, yi,     zi), hsh(xi + 1, yi,     zi), u),
                   lrp(hsh(xi, yi + 1, zi), hsh(xi + 1, yi + 1, zi), u), v);
    float z1 = lrp(lrp(hsh(xi, yi,     zi + 1), hsh(xi + 1, yi,     zi + 1), u),
                   lrp(hsh(xi, yi + 1, zi + 1), hsh(xi + 1, yi + 1, zi + 1), u), v);

    return lrp(z0, z1, w);
}

vec3 hsv(float h, float s, float v)
{
    h = fr(h) * 6.0;
    s = clamp(s, 0.0, 1.0);

    int sector = int(h);
    float f = h - float(sector);
    float p = v * (1.0 - s);
    float q = v * (1.0 - s * f);
    float w = v * (1.0 - s * (1.0 - f));

    if (sector == 0) return vec3(v, w, p);
    if (sector == 1) return vec3(q, v, p);
    if (sector == 2) return vec3(p, v, w);
    if (sector == 3) return vec3(p, q, v);
    if (sector == 4) return vec3(w, p, v);
    return vec3(v, p, q);
}

void main()
{
    float px = (vUv.x * 2.0 - 1.0) * uAspect;
    float py = vUv.y * 2.0 - 1.0;

    float r0 = uK[0];
    float r1 = uK[1];
    float r2 = px;
    float r3 = py;
    vec2 w7 = vec2(uTime, uTimeLo); float r7 = w7.x + w7.y;
    float r8 = uK[2];
    vec2 w9 = dml(w7, vec2(r8, 0.0)); float r9 = w9.x + w9.y;
    float r10 = cos(dtr(w9));
    float r11 = sin(dtr(w9));
    float r12 = r2 * r10;
    float r13 = r3 * r11;
    float r14 = r12 - r13;
    float r15 = r2 * r11;
    float r16 = r3 * r10;
    float r17 = r15 + r16;
    float r18 = uK[3];
    float r19 = sqrt(r14 * r14 + r17 * r17);
    float r20 = at2(r17, r14);
    float r21 = uK[4];
    float r22 = dv(r21, r18);
    float r23 = uK[5];
    float r24 = r22 * r23;
    float r25 = md(r20, r22);
    float r26 = r25 - r24;
    float r27 = abs(r26);
    float r28 = cos(r27);
    float r29 = r28 * r19;
    float r30 = sin(r27);
    float r31 = r30 * r19;
    float r32 = uK[6];
    float r33 = uK[7];
    float r34 = r29 - r32;
    float r35 = r31 - r33;
    float r36 = uK[8];
    vec2 w37 = dad(dml(w7, vec2(r36, 0.0)), vec2(r33, 0.0)); float r37 = w37.x + w37.y;
    vec2 w38 = dml(w37, vec2(r21, 0.0)); float r38 = w38.x + w38.y;
    float r39 = sin(dtr(w38));
    float r40 = r39 * r1;
    float r41 = r40 + r33;
    float r42 = uK[9];
    float r43 = uK[10];
    float r44 = r41 - r42;
    float r45 = r1 - r42;
    float r46 = dv(r44, r45);
    float r47 = r33 + (r43 - r33) * r46;
    float r48 = r1 + r47;
    float r49 = r34 * r48;
    float r50 = r35 * r48;
    float r51 = uK[11];
    float r52 = uK[12];
    vec2 w53 = dml(w7, vec2(r52, 0.0)); float r53 = w53.x + w53.y;
    float r54 = sqrt(r49 * r49 + r50 * r50);
    vec2 w55 = dml(vec2(r54, 0.0), vec2(r51, 0.0)); float r55 = w55.x + w55.y;
    vec2 w56 = dad(w55, w53); float r56 = w56.x + w56.y;
    vec2 w57 = dml(w56, vec2(r21, 0.0)); float r57 = w57.x + w57.y;
    float r58 = sin(dtr(w57));
    float r59 = sm(r0, r1, r58);
    float r60 = sqrt(r34 * r34 + r35 * r35);
    vec2 w61 = dml(vec2(r60, 0.0), vec2(r51, 0.0)); float r61 = w61.x + w61.y;
    vec2 w62 = dad(w61, w53); float r62 = w62.x + w62.y;
    vec2 w63 = dml(w62, vec2(r21, 0.0)); float r63 = w63.x + w63.y;
    float r64 = sin(dtr(w63));
    float r65 = sm(r0, r1, r64);
    float r66 = r1 - r47;
    float r67 = r34 * r66;
    float r68 = r35 * r66;
    float r69 = sqrt(r67 * r67 + r68 * r68);
    vec2 w70 = dml(vec2(r69, 0.0), vec2(r51, 0.0)); float r70 = w70.x + w70.y;
    vec2 w71 = dad(w70, w53); float r71 = w71.x + w71.y;
    vec2 w72 = dml(w71, vec2(r21, 0.0)); float r72 = w72.x + w72.y;
    float r73 = sin(dtr(w72));
    float r74 = sm(r0, r1, r73);
    float r75 = r59;
    float r76 = r65;
    float r77 = r74;

    fragColor = vec4(sat(r75), sat(r76), sat(r77), 1.0);
}
