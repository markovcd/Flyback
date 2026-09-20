#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
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
    float r7 = uTime;
    float r8 = uK[2];
    float r9 = r7 * r8;
    float r10 = cos(r9);
    float r11 = sin(r9);
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
    float r37 = r7 * r36 + r33;
    float r38 = r37 * r21;
    float r39 = sin(r38);
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
    float r53 = r7 * r52;
    float r54 = sqrt(r49 * r49 + r50 * r50);
    float r55 = r54 * r51;
    float r56 = r55 + r53;
    float r57 = r56 * r21;
    float r58 = sin(r57);
    float r59 = sm(r0, r1, r58);
    float r60 = sqrt(r34 * r34 + r35 * r35);
    float r61 = r60 * r51;
    float r62 = r61 + r53;
    float r63 = r62 * r21;
    float r64 = sin(r63);
    float r65 = sm(r0, r1, r64);
    float r66 = r1 - r47;
    float r67 = r34 * r66;
    float r68 = r35 * r66;
    float r69 = sqrt(r67 * r67 + r68 * r68);
    float r70 = r69 * r51;
    float r71 = r70 + r53;
    float r72 = r71 * r21;
    float r73 = sin(r72);
    float r74 = sm(r0, r1, r73);
    float r75 = r59;
    float r76 = r65;
    float r77 = r74;

    fragColor = vec4(sat(r75), sat(r76), sat(r77), 1.0);
}
