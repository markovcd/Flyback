#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
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
    float r4 = sqrt(r2 * r2 + r3 * r3);
    float r5 = at2(r3, r2);
    float r6 = uK[2];
    float r7 = r4 * r1 + r0;
    float r8 = uK[3];
    float r9 = r7 * r8;
    float r10 = sin(r9);
    float r11 = r10 * r6;
    float r12 = r11 + r6;
    vec3 t13 = hsv(r0, r1, r12);
    float r13 = t13.x; float r14 = t13.y; float r15 = t13.z;
    float r16 = uTime;
    float r17 = uK[4];
    float r18 = r16 * r17 + r0;
    float r19 = r18 * r8;
    float r20 = sin(r19);
    float r21 = r20 * r6;
    float r22 = r21 + r6;
    float r23 = uK[5];
    float r24 = uK[6];
    float r25 = r4 * r24 + r0;
    float r26 = r25 * r8;
    float r27 = sin(r26);
    float r28 = r27 * r6;
    float r29 = r28 + r6;
    vec3 t30 = hsv(r23, r1, r29);
    float r30 = t30.x; float r31 = t30.y; float r32 = t30.z;
    float r33 = uK[7];
    float r34 = uK[8];
    float r35 = r16 * r33 + r34;
    float r36 = r35 * r8;
    float r37 = sin(r36);
    float r38 = r37 * r6;
    float r39 = r38 + r6;
    float r40 = uK[9];
    float r41 = uK[10];
    float r42 = r4 * r41 + r0;
    float r43 = r42 * r8;
    float r44 = sin(r43);
    float r45 = r44 * r6;
    float r46 = r45 + r6;
    vec3 t47 = hsv(r40, r1, r46);
    float r47 = t47.x; float r48 = t47.y; float r49 = t47.z;
    float r50 = uK[11];
    float r51 = uK[12];
    float r52 = r16 * r50 + r51;
    float r53 = r52 * r8;
    float r54 = sin(r53);
    float r55 = r54 * r6;
    float r56 = r55 + r6;
    float r57 = uK[13];
    float r58 = uK[14];
    float r59 = r4 * r58 + r0;
    float r60 = r59 * r8;
    float r61 = sin(r60);
    float r62 = r61 * r6;
    float r63 = r62 + r6;
    vec3 t64 = hsv(r57, r1, r63);
    float r64 = t64.x; float r65 = t64.y; float r66 = t64.z;
    float r67 = uK[15];
    float r68 = uK[16];
    float r69 = r16 * r67 + r68;
    float r70 = r69 * r8;
    float r71 = sin(r70);
    float r72 = r71 * r6;
    float r73 = r72 + r6;
    float r74 = r13 * r22;
    float r75 = r14 * r22;
    float r76 = r15 * r22;
    float r77 = r30 * r39;
    float r78 = r31 * r39;
    float r79 = r32 * r39;
    float r80 = r74 + r77;
    float r81 = r75 + r78;
    float r82 = r76 + r79;
    float r83 = r47 * r56;
    float r84 = r48 * r56;
    float r85 = r49 * r56;
    float r86 = r80 + r83;
    float r87 = r81 + r84;
    float r88 = r82 + r85;
    float r89 = r64 * r73;
    float r90 = r65 * r73;
    float r91 = r66 * r73;
    float r92 = r86 + r89;
    float r93 = r87 + r90;
    float r94 = r88 + r91;
    float r95 = r92 * r51;
    float r96 = r93 * r51;
    float r97 = r94 * r51;
    float r98 = r95 + r0;
    float r99 = r96 + r0;
    float r100 = r97 + r0;
    float r101 = r0 * r0;
    float r102 = r0 * r0;

    fragColor = vec4(sat(r98), sat(r99), sat(r100), 1.0);
}
