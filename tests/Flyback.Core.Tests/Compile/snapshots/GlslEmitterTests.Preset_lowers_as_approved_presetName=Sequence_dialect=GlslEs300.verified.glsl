#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uTimeLo;
uniform float uOne;
uniform float uAspect;
uniform float uK[20];

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

    vec2 w0 = vec2(uTime, uTimeLo); float r0 = w0.x + w0.y;
    float r3 = uK[0];
    float r4 = uK[1];
    float r5 = uK[2];
    vec2 w6 = dml(w0, vec2(r3, 0.0)); float r6 = w6.x + w6.y;
    float r7 = uK[3];
    float r8 = dmd(w6, vec2(r7, 0.0));
    float r9 = floor(r8);
    float r10 = dfr(w6);
    float r11 = dv(r9, r7);
    float r12 = uK[4];
    float r13 = uK[5];
    float r14 = step(r12, r9);
    float r15 = uK[6];
    float r16 = step(r15, r9);
    float r17 = step(r3, r9);
    float r18 = uK[7];
    float r19 = step(r18, r9);
    float r20 = uK[8];
    float r21 = step(r20, r9);
    float r22 = uK[9];
    float r23 = step(r22, r9);
    float r24 = uK[10];
    float r25 = step(r24, r9);
    float r26 = r12 - r14;
    float r29 = r26 * r12;
    float r30 = r14 - r16;
    float r33 = r30 * r12;
    float r35 = r29 + r33;
    float r36 = r16 - r17;
    float r39 = r36 * r12;
    float r41 = r35 + r39;
    float r42 = r17 - r19;
    float r45 = r42 * r12;
    float r47 = r41 + r45;
    float r48 = r19 - r21;
    float r51 = r48 * r12;
    float r53 = r47 + r51;
    float r54 = r21 - r23;
    float r56 = r54 * r12;
    float r58 = r53 + r56;
    float r59 = r23 - r25;
    float r61 = r59 * r12;
    float r63 = r58 + r61;
    float r64 = r25 - r13;
    float r66 = r64 * r12;
    float r68 = r63 + r66;
    float r69 = uK[11];
    float r70 = uK[12];
    float r71 = clamp(r5, r69, max(r69, r70));
    float r72 = clamp(r4, r13, max(r13, r12));
    float r73 = sm(r13, r71, r10);
    float r74 = r72 - r71;
    float r75 = sm(r74, r72, r10);
    float r76 = r12 - r75;
    float r77 = r68 * r73;
    float r78 = r77 * r76;
    float r79 = uK[13];
    float r80 = px;
    float r81 = py;
    float r85 = uK[14];
    float r86 = uK[15];
    float r87 = r11 - r13;
    float r88 = r12 - r13;
    float r89 = dv(r87, r88);
    float r90 = r85 + (r86 - r85) * r89;
    float r91 = sqrt(r80 * r80 + r81 * r81);
    float r92 = r91 * r90;
    float r93 = r92 + r13;
    float r94 = uK[16];
    float r95 = r93 * r94;
    float r96 = sin(r95);
    float r97 = uK[17];
    float r98 = uK[18];
    float r99 = r96 - r97;
    float r100 = r12 - r97;
    float r101 = dv(r99, r100);
    float r102 = r98 + (r12 - r98) * r101;
    float r103 = uK[19];
    float r104 = r78 - r13;
    float r105 = r12 - r13;
    float r106 = dv(r104, r105);
    float r107 = r103 + (r12 - r103) * r106;
    float r108 = r102 * r107;
    vec3 t109 = hsv(r11, r79, r108);
    float r109 = t109.x; float r110 = t109.y; float r111 = t109.z;

    fragColor = vec4(sat(r109), sat(r110), sat(r111), 1.0);
}
