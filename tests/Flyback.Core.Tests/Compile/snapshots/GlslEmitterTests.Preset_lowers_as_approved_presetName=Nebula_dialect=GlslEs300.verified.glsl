#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[14];

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

uniform sampler2D uPrevious;
uniform float uFeedbackScaleX;
uniform float uFeedbackScaleY;

vec3 fb(float u, float v)
{
    u = fin(u) ? u : -uAspect;
    v = fin(v) ? v :  1.0;

    return texture(uPrevious, vec2(u * uFeedbackScaleX + 0.5, v * uFeedbackScaleY + 0.5)).rgb;
}

void main()
{
    float px = (vUv.x * 2.0 - 1.0) * uAspect;
    float py = vUv.y * 2.0 - 1.0;

    float r0 = px;
    float r1 = py;
    float r2 = sqrt(r0 * r0 + r1 * r1);
    float r3 = at2(r1, r0);
    float r4 = uAspect;
    float r5 = uK[0];
    float r6 = r0 * r5;
    float r7 = r1 * r5;
    float r8 = uK[1];
    float r9 = cos(r8);
    float r10 = sin(r8);
    float r11 = r6 * r9;
    float r12 = r7 * r10;
    float r13 = r11 - r12;
    float r14 = r6 * r10;
    float r15 = r7 * r9;
    float r16 = r14 + r15;
    vec3 t17 = fb(r13, r16);
    float r17 = t17.x; float r18 = t17.y; float r19 = t17.z;
    float r20 = uK[2];
    float r21 = uK[3];
    float r22 = r17 * r20;
    float r23 = r18 * r20;
    float r24 = r19 * r20;
    float r25 = r22 + r21;
    float r26 = r23 + r21;
    float r27 = r24 + r21;
    float r28 = uTime;
    float r29 = uK[4];
    float r30 = r28 * r29;
    float r31 = cos(r30);
    float r32 = sin(r30);
    float r33 = r0 * r31;
    float r34 = r1 * r32;
    float r35 = r33 - r34;
    float r36 = r0 * r32;
    float r37 = r1 * r31;
    float r38 = r36 + r37;
    float r39 = uK[5];
    float r40 = sqrt(r35 * r35 + r38 * r38);
    float r41 = at2(r38, r35);
    float r42 = uK[6];
    float r43 = dv(r42, r39);
    float r44 = uK[7];
    float r45 = r43 * r44;
    float r46 = md(r41, r43);
    float r47 = r46 - r45;
    float r48 = abs(r47);
    float r49 = cos(r48);
    float r50 = r49 * r40;
    float r51 = sin(r48);
    float r52 = r51 * r40;
    float r53 = uK[8];
    float r54 = r28 * r53;
    float r55 = uK[9];
    float r56 = r50 * r55;
    float r57 = r52 * r55;
    float r58 = nz(r56, r57, r54);
    float r59 = uK[10];
    float r60 = r28 * r59;
    float r61 = r58 + r60;
    float r62 = fr(r61);
    float r63 = uK[11];
    float r64 = uK[12];
    float r65 = r58 * r44;
    float r66 = r50 + r65;
    float r67 = r65 * r42;
    float r68 = sin(r67);
    float r69 = r52 + r68;
    float r70 = uK[13];
    float r71 = sqrt(r66 * r66 + r69 * r69);
    float r72 = r71 * r70;
    float r73 = r72 + r60;
    float r74 = r73 * r42;
    float r75 = sin(r74);
    float r76 = sm(r64, r63, r75);
    vec3 t77 = hsv(r62, r63, r76);
    float r77 = t77.x; float r78 = t77.y; float r79 = t77.z;
    float r80 = max(r25, r77);
    float r81 = max(r26, r78);
    float r82 = max(r27, r79);
    float r83 = r21 * r21;
    float r84 = r21 * r21;

    fragColor = vec4(sat(r80), sat(r81), sat(r82), 1.0);
}
