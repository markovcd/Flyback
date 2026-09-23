#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uTimeLo;
uniform float uOne;
uniform float uAspect;
uniform float uK[17];

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
    float r4 = sqrt(r2 * r2 + r3 * r3);
    float r7 = uK[2];
    float r8 = r4 * r1 + r0;
    float r9 = uK[3];
    float r10 = r8 * r9;
    float r11 = sin(r10);
    float r12 = r11 * r7;
    float r13 = r12 + r7;
    vec3 t14 = hsv(r0, r1, r13);
    float r14 = t14.x; float r15 = t14.y; float r16 = t14.z;
    vec2 w17 = vec2(uTime, uTimeLo); float r17 = w17.x + w17.y;
    float r18 = uK[4];
    vec2 w19 = dad(dml(w17, vec2(r18, 0.0)), vec2(r0, 0.0)); float r19 = w19.x + w19.y;
    vec2 w20 = dml(w19, vec2(r9, 0.0)); float r20 = w20.x + w20.y;
    float r21 = sin(dtr(w20));
    float r22 = r21 * r7;
    float r23 = r22 + r7;
    float r24 = uK[5];
    float r25 = uK[6];
    float r26 = r4 * r25 + r0;
    float r27 = r26 * r9;
    float r28 = sin(r27);
    float r29 = r28 * r7;
    float r30 = r29 + r7;
    vec3 t31 = hsv(r24, r1, r30);
    float r31 = t31.x; float r32 = t31.y; float r33 = t31.z;
    float r34 = uK[7];
    float r35 = uK[8];
    vec2 w36 = dad(dml(w17, vec2(r34, 0.0)), vec2(r35, 0.0)); float r36 = w36.x + w36.y;
    vec2 w37 = dml(w36, vec2(r9, 0.0)); float r37 = w37.x + w37.y;
    float r38 = sin(dtr(w37));
    float r39 = r38 * r7;
    float r40 = r39 + r7;
    float r41 = uK[9];
    float r42 = uK[10];
    float r43 = r4 * r42 + r0;
    float r44 = r43 * r9;
    float r45 = sin(r44);
    float r46 = r45 * r7;
    float r47 = r46 + r7;
    vec3 t48 = hsv(r41, r1, r47);
    float r48 = t48.x; float r49 = t48.y; float r50 = t48.z;
    float r51 = uK[11];
    float r52 = uK[12];
    vec2 w53 = dad(dml(w17, vec2(r51, 0.0)), vec2(r52, 0.0)); float r53 = w53.x + w53.y;
    vec2 w54 = dml(w53, vec2(r9, 0.0)); float r54 = w54.x + w54.y;
    float r55 = sin(dtr(w54));
    float r56 = r55 * r7;
    float r57 = r56 + r7;
    float r58 = uK[13];
    float r59 = uK[14];
    float r60 = r4 * r59 + r0;
    float r61 = r60 * r9;
    float r62 = sin(r61);
    float r63 = r62 * r7;
    float r64 = r63 + r7;
    vec3 t65 = hsv(r58, r1, r64);
    float r65 = t65.x; float r66 = t65.y; float r67 = t65.z;
    float r68 = uK[15];
    float r69 = uK[16];
    vec2 w70 = dad(dml(w17, vec2(r68, 0.0)), vec2(r69, 0.0)); float r70 = w70.x + w70.y;
    vec2 w71 = dml(w70, vec2(r9, 0.0)); float r71 = w71.x + w71.y;
    float r72 = sin(dtr(w71));
    float r73 = r72 * r7;
    float r74 = r73 + r7;
    float r75 = r14 * r23;
    float r76 = r15 * r23;
    float r77 = r16 * r23;
    float r78 = r31 * r40;
    float r79 = r32 * r40;
    float r80 = r33 * r40;
    float r81 = r75 + r78;
    float r82 = r76 + r79;
    float r83 = r77 + r80;
    float r84 = r48 * r57;
    float r85 = r49 * r57;
    float r86 = r50 * r57;
    float r87 = r81 + r84;
    float r88 = r82 + r85;
    float r89 = r83 + r86;
    float r90 = r65 * r74;
    float r91 = r66 * r74;
    float r92 = r67 * r74;
    float r93 = r87 + r90;
    float r94 = r88 + r91;
    float r95 = r89 + r92;
    float r96 = r93 * r52;
    float r97 = r94 * r52;
    float r98 = r95 * r52;
    float r99 = r96 + r0;
    float r100 = r97 + r0;
    float r101 = r98 + r0;

    fragColor = vec4(sat(r99), sat(r100), sat(r101), 1.0);
}
