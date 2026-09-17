#version 150

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
    float r5 = uTime;
    float r6 = uK[0];
    float r7 = r5 * r6;
    float r8 = cos(r7);
    float r9 = sin(r7);
    float r10 = r0 * r8;
    float r11 = r1 * r9;
    float r12 = r10 - r11;
    float r13 = r0 * r9;
    float r14 = r1 * r8;
    float r15 = r13 + r14;
    float r16 = uK[1];
    float r17 = sqrt(r12 * r12 + r15 * r15);
    float r18 = at2(r15, r12);
    float r19 = uK[2];
    float r20 = dv(r19, r16);
    float r21 = uK[3];
    float r22 = r20 * r21;
    float r23 = md(r18, r20);
    float r24 = r23 - r22;
    float r25 = abs(r24);
    float r26 = cos(r25);
    float r27 = r26 * r17;
    float r28 = sin(r25);
    float r29 = r28 * r17;
    float r30 = uK[4];
    float r31 = r5 * r30;
    float r32 = uK[5];
    float r33 = r27 * r32;
    float r34 = r29 * r32;
    float r35 = nz(r33, r34, r31);
    float r36 = uK[6];
    float r37 = r5 * r36;
    float r38 = r35 + r37;
    float r39 = fr(r38);
    float r40 = uK[7];
    float r41 = uK[8];
    float r42 = r35 * r21;
    float r43 = r27 + r42;
    float r44 = r42 * r19;
    float r45 = sin(r44);
    float r46 = r29 + r45;
    float r47 = uK[9];
    float r48 = sqrt(r43 * r43 + r46 * r46);
    float r49 = r48 * r47;
    float r50 = r49 + r37;
    float r51 = r50 * r19;
    float r52 = sin(r51);
    float r53 = sm(r41, r40, r52);
    vec3 t54 = hsv(r39, r40, r53);
    float r54 = t54.x; float r55 = t54.y; float r56 = t54.z;
    float r57 = uK[10];
    float r58 = uK[11];
    float r59 = uK[12];
    float r60 = uK[13];
    float r61 = r0 * r57;
    float r62 = r1 * r57;
    float r63 = cos(r58);
    float r64 = sin(r58);
    float r65 = r61 * r63;
    float r66 = r62 * r64;
    float r67 = r65 - r66;
    float r68 = r61 * r64;
    float r69 = r62 * r63;
    float r70 = r68 + r69;
    float r71 = r67 - r59;
    float r72 = r70 - r59;
    vec3 t73 = fb(r71, r72);
    float r73 = t73.x; float r74 = t73.y; float r75 = t73.z;
    float r76 = r73 * r60;
    float r77 = r74 * r60;
    float r78 = r75 * r60;
    float r79 = max(r76, r54);
    float r80 = max(r77, r55);
    float r81 = max(r78, r56);

    fragColor = vec4(sat(r79), sat(r80), sat(r81), 1.0);
}
