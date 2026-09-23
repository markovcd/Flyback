#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uTimeLo;
uniform float uOne;
uniform float uAspect;
uniform float uK[27];

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
    float r2 = uK[1];
    float r3 = uK[2];
    float r4 = px;
    float r5 = py;
    float r6 = uAspect;
    float r7 = r4 + r6;
    float r8 = uK[3];
    float r9 = r6 * r8;
    float r10 = dv(r7, r9);
    float r11 = uK[4];
    float r12 = r10 * r11;
    float r13 = 0.0;
    float r14 = uK[5];
    float r15 = max(r3, r14);
    float r16 = dv(r13, r15);
    float r17 = uK[6];
    float r18 = max(r16, r17);
    float r19 = lg(r18);
    float r20 = uK[7];
    float r21 = r19 * r20;
    float r22 = max(r2, r3);
    float r23 = dv(r21, r22);
    float r24 = r23 * r8;
    float r25 = r24 + r3;
    float r26 = uK[8];
    float r27 = max(r25, r26);
    float r28 = uK[9];
    float r29 = r6 * r28;
    float r30 = r4 + r29;
    float r31 = uK[10];
    float r32 = r6 * r31;
    float r33 = uK[11];
    float r34 = uK[12];
    float r35 = uK[13];
    float r36 = r5 - r27;
    float r37 = abs(r36);
    float r38 = sm(r34, r35, r37);
    float r39 = r3 - r38;
    float r40 = min(r27, r33);
    float r41 = step(r40, r5);
    float r42 = max(r27, r33);
    float r43 = step(r5, r42);
    float r44 = r41 * r43;
    float r45 = abs(r27);
    float r46 = step(r3, r45);
    float r47 = uK[14];
    float r48 = abs(r5);
    float r49 = step(r47, r48);
    float r50 = r27 * r5;
    float r51 = step(r0, r50);
    float r52 = r46 * r49;
    float r53 = r52 * r51;
    float r54 = uK[15];
    float r55 = r44 * r54;
    float r56 = r55 + r39;
    float r57 = dv(r30, r32);
    float r58 = uK[16];
    float r59 = r57 + r58;
    float r60 = fr(r59);
    float r61 = uK[17];
    float r62 = r60 + r61;
    float r63 = abs(r62);
    float r64 = r63 * r32;
    float r65 = uK[18];
    float r66 = uK[19];
    float r67 = sm(r65, r66, r64);
    float r68 = r3 - r67;
    float r69 = uK[20];
    float r70 = dv(r5, r69);
    float r71 = r70 + r58;
    float r72 = fr(r71);
    float r73 = r72 + r61;
    float r74 = abs(r73);
    float r75 = r74 * r69;
    float r76 = sm(r65, r66, r75);
    float r77 = r3 - r76;
    float r78 = r68 + r77;
    float r79 = uK[21];
    float r80 = uK[22];
    float r81 = abs(r5);
    float r82 = sm(r79, r80, r81);
    float r83 = r3 - r82;
    float r84 = uK[23];
    float r85 = r78 * r84;
    float r86 = uK[24];
    float r87 = r83 * r86;
    float r88 = r85 + r87;
    float r89 = r88 + r56;
    float r90 = uK[25];
    float r91 = uK[26];
    float r92 = r90;
    float r93 = r3;
    float r94 = r91;
    float r95 = r92 * r89;
    float r96 = r93 * r89;
    float r97 = r94 * r89;
    float r98 = r3;
    float r99 = r69;
    float r100 = r86;
    float r101 = r98 * r53;
    float r102 = r99 * r53;
    float r103 = r100 * r53;
    float r104 = r95 + r101;
    float r105 = r96 + r102;
    float r106 = r97 + r103;

    fragColor = vec4(sat(r104), sat(r105), sat(r106), 1.0);
}
