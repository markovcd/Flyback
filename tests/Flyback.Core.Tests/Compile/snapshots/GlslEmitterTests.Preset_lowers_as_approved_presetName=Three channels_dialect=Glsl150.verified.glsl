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
    float r10 = uK[2];
    vec2 w11 = dml(w7, vec2(r10, 0.0)); float r11 = w11.x + w11.y;
    float r12 = cos(dtr(w11));
    float r13 = sin(dtr(w11));
    float r14 = r2 * r12;
    float r15 = r3 * r13;
    float r16 = r14 - r15;
    float r17 = r2 * r13;
    float r18 = r3 * r12;
    float r19 = r17 + r18;
    float r20 = uK[3];
    float r21 = sqrt(r16 * r16 + r19 * r19);
    float r22 = at2(r19, r16);
    float r23 = uK[4];
    float r24 = dv(r23, r20);
    float r25 = uK[5];
    float r26 = r24 * r25;
    float r27 = md(r22, r24);
    float r28 = r27 - r26;
    float r29 = abs(r28);
    float r30 = cos(r29);
    float r31 = r30 * r21;
    float r32 = sin(r29);
    float r33 = r32 * r21;
    float r34 = uK[6];
    float r35 = uK[7];
    float r36 = r31 - r34;
    float r37 = r33 - r35;
    float r39 = uK[8];
    vec2 w40 = dad(dml(w7, vec2(r39, 0.0)), vec2(r35, 0.0)); float r40 = w40.x + w40.y;
    vec2 w41 = dml(w40, vec2(r23, 0.0)); float r41 = w41.x + w41.y;
    float r42 = sin(dtr(w41));
    float r43 = r42 * r1;
    float r44 = r43 + r35;
    float r45 = uK[9];
    float r46 = uK[10];
    float r47 = r44 - r45;
    float r48 = r1 - r45;
    float r49 = dv(r47, r48);
    float r50 = r35 + (r46 - r35) * r49;
    float r51 = r1 + r50;
    float r52 = r36 * r51;
    float r53 = r37 * r51;
    float r54 = uK[11];
    float r55 = uK[12];
    vec2 w56 = dml(w7, vec2(r55, 0.0)); float r56 = w56.x + w56.y;
    float r57 = sqrt(r52 * r52 + r53 * r53);
    vec2 w58 = dml(vec2(r57, 0.0), vec2(r54, 0.0)); float r58 = w58.x + w58.y;
    vec2 w59 = dad(w58, w56); float r59 = w59.x + w59.y;
    vec2 w60 = dml(w59, vec2(r23, 0.0)); float r60 = w60.x + w60.y;
    float r61 = sin(dtr(w60));
    float r62 = sm(r0, r1, r61);
    float r63 = sqrt(r36 * r36 + r37 * r37);
    vec2 w64 = dml(vec2(r63, 0.0), vec2(r54, 0.0)); float r64 = w64.x + w64.y;
    vec2 w65 = dad(w64, w56); float r65 = w65.x + w65.y;
    vec2 w66 = dml(w65, vec2(r23, 0.0)); float r66 = w66.x + w66.y;
    float r67 = sin(dtr(w66));
    float r68 = sm(r0, r1, r67);
    float r69 = r1 - r50;
    float r70 = r36 * r69;
    float r71 = r37 * r69;
    float r72 = sqrt(r70 * r70 + r71 * r71);
    vec2 w73 = dml(vec2(r72, 0.0), vec2(r54, 0.0)); float r73 = w73.x + w73.y;
    vec2 w74 = dad(w73, w56); float r74 = w74.x + w74.y;
    vec2 w75 = dml(w74, vec2(r23, 0.0)); float r75 = w75.x + w75.y;
    float r76 = sin(dtr(w75));
    float r77 = sm(r0, r1, r76);
    float r78 = r62;
    float r79 = r68;
    float r80 = r77;

    fragColor = vec4(sat(r78), sat(r79), sat(r80), 1.0);
}
