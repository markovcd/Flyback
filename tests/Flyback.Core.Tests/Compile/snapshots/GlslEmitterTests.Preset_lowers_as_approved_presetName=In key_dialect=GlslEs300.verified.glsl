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

    float r0 = px;
    float r1 = py;
    vec2 w5 = vec2(uTime, uTimeLo); float r5 = w5.x + w5.y;
    float r6 = uK[0];
    float r8 = uK[1];
    vec2 w9 = dml(w5, vec2(r8, 0.0)); float r9 = w9.x + w9.y;
    float r10 = uK[2];
    float r11 = r0 * r10;
    float r12 = r1 * r10;
    float r13 = nzw(vec2(r11, 0.0), vec2(r12, 0.0), w9);
    float r14 = uK[3];
    float r15 = uK[4];
    float r16 = uK[5];
    float r17 = uK[6];
    float r18 = r13 - r14;
    float r19 = r15 - r14;
    float r20 = dv(r18, r19);
    float r21 = r16 + (r17 - r16) * r20;
    float r23 = uK[7];
    vec2 w24 = dml(vec2(r6, 0.0), vec2(r23, 0.0)); float r24 = w24.x + w24.y;
    float r26 = uK[8];
    vec2 w27 = dad(dml(w5, w24), vec2(r14, 0.0)); float r27 = w27.x + w27.y;
    float r28 = dfr(w27);
    float r29 = step(r26, r28);
    float r30 = uK[9];
    float r31 = r29 * r30;
    float r32 = uK[10];
    float r33 = r31 + r32;
    float r34 = r33 * r15;
    float r35 = r34 + r14;
    float r36 = uK[11];
    float r37 = r21 * r36;
    float r38 = uK[12];
    float r39 = r37 + r38;
    float r40 = floor(r39);
    float r41 = uK[13];
    float r42 = r40 * r41;
    float r43 = r42 + r14;
    float r44 = r21 - r43;
    float r45 = abs(r44);
    float r46 = uK[14];
    float r47 = r37 + r46;
    float r48 = floor(r47);
    float r49 = r48 * r41;
    float r50 = r49 + r30;
    float r51 = r21 - r50;
    float r52 = abs(r51);
    float r53 = step(r52, r45);
    float r54 = r43 + (r50 - r43) * r53;
    float r55 = r45 + (r52 - r45) * r53;
    float r56 = uK[15];
    float r57 = r37 + r56;
    float r58 = floor(r57);
    float r59 = r58 * r41;
    float r60 = uK[16];
    float r61 = r59 + r60;
    float r62 = r21 - r61;
    float r63 = abs(r62);
    float r64 = step(r63, r55);
    float r65 = r54 + (r61 - r54) * r64;
    float r66 = r55 + (r63 - r55) * r64;
    float r67 = uK[17];
    float r68 = r37 + r67;
    float r69 = floor(r68);
    float r70 = r69 * r41;
    float r71 = uK[18];
    float r72 = r70 + r71;
    float r73 = r21 - r72;
    float r74 = abs(r73);
    float r75 = step(r74, r66);
    float r76 = r65 + (r72 - r65) * r75;
    float r77 = r66 + (r74 - r66) * r75;
    float r78 = uK[19];
    float r79 = r37 + r78;
    float r80 = floor(r79);
    float r81 = r80 * r41;
    float r82 = uK[20];
    float r83 = r81 + r82;
    float r84 = r21 - r83;
    float r85 = abs(r84);
    float r86 = step(r85, r77);
    float r87 = r76 + (r83 - r76) * r86;
    float r89 = 0.0;
    float r90 = 0.0;
    float r91 = uK[21];
    float r92 = r90 * r91;
    float r93 = 0.0;
    float r94 = step(r38, r35);
    float r95 = r15 - r93;
    float r96 = r94 * r95;
    float r97 = r15 - r94;
    float r98 = max(r96, r97);
    float r99 = r15 - r89;
    float r100 = max(r98, r99);
    float r101 = r92 + (r87 - r92) * r100;
    float r102 = uK[22];
    float r103 = r101 * r102;
    float r104 = r101 * r36;
    float r105 = fr(r104);
    float r106 = uK[23];
    float r107 = uK[24];
    float r108 = r105 - r14;
    float r109 = r15 - r14;
    float r110 = dv(r108, r109);
    float r111 = r106 + (r107 - r106) * r110;
    float r112 = uK[25];
    float r113 = uK[26];
    float r114 = r13 - r14;
    float r115 = r15 - r14;
    float r116 = dv(r114, r115);
    float r117 = r112 + (r113 - r112) * r116;
    vec3 t118 = hsv(r111, r107, r117);
    float r118 = t118.x; float r119 = t118.y; float r120 = t118.z;

    fragColor = vec4(sat(r118), sat(r119), sat(r120), 1.0);
}
