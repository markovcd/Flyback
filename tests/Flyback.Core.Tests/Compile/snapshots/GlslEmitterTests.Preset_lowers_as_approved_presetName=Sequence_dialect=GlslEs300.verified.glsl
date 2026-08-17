#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[24];

in vec2 vUv;
out vec4 fragColour;

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
    float r1 = uTime;
    float r2 = r1 * r0;
    float r3 = uK[1];
    float r4 = uK[2];
    float r5 = uK[3];
    float r6 = r2 * r3;
    float r7 = uK[4];
    float r8 = md(r6, r7);
    float r9 = floor(r8);
    float r10 = fr(r6);
    float r11 = dv(r9, r7);
    float r12 = uK[5];
    float r13 = step(r0, r9);
    float r14 = uK[6];
    float r15 = step(r14, r9);
    float r16 = step(r3, r9);
    float r17 = uK[7];
    float r18 = step(r17, r9);
    float r19 = uK[8];
    float r20 = step(r19, r9);
    float r21 = uK[9];
    float r22 = step(r21, r9);
    float r23 = uK[10];
    float r24 = step(r23, r9);
    float r25 = r0 - r13;
    float r26 = uK[11];
    float r27 = r25 * r26;
    float r28 = r25 * r0;
    float r29 = r13 - r15;
    float r30 = uK[12];
    float r31 = r29 * r30;
    float r32 = r29 * r0;
    float r33 = r27 + r31;
    float r34 = r28 + r32;
    float r35 = r15 - r16;
    float r36 = uK[13];
    float r37 = r35 * r36;
    float r38 = r35 * r0;
    float r39 = r33 + r37;
    float r40 = r34 + r38;
    float r41 = r16 - r18;
    float r42 = uK[14];
    float r43 = r41 * r42;
    float r44 = r41 * r0;
    float r45 = r39 + r43;
    float r46 = r40 + r44;
    float r47 = r18 - r20;
    float r48 = uK[15];
    float r49 = r47 * r48;
    float r50 = r47 * r0;
    float r51 = r45 + r49;
    float r52 = r46 + r50;
    float r53 = r20 - r22;
    float r54 = r53 * r42;
    float r55 = r53 * r0;
    float r56 = r51 + r54;
    float r57 = r52 + r55;
    float r58 = r22 - r24;
    float r59 = r58 * r36;
    float r60 = r58 * r0;
    float r61 = r56 + r59;
    float r62 = r57 + r60;
    float r63 = r24 - r12;
    float r64 = r63 * r30;
    float r65 = r63 * r0;
    float r66 = r61 + r64;
    float r67 = r62 + r65;
    float r68 = uK[16];
    float r69 = clamp(r5, r68, max(r68, r0));
    float r70 = clamp(r4, r12, max(r12, r0));
    float r71 = sm(r12, r69, r10);
    float r72 = r70 - r69;
    float r73 = sm(r72, r70, r10);
    float r74 = r0 - r73;
    float r75 = r67 * r71;
    float r76 = r75 * r74;
    float r77 = uK[17];
    float r78 = px;
    float r79 = py;
    float r80 = sqrt(r78 * r78 + r79 * r79);
    float r81 = at2(r79, r78);
    float r82 = uK[18];
    float r83 = uK[19];
    float r84 = r11 - r12;
    float r85 = r0 - r12;
    float r86 = dv(r84, r85);
    float r87 = r82 + (r83 - r82) * r86;
    float r88 = sqrt(r78 * r78 + r79 * r79);
    float r89 = r88 * r87;
    float r90 = r89 + r12;
    float r91 = uK[20];
    float r92 = r90 * r91;
    float r93 = sin(r92);
    float r94 = uK[21];
    float r95 = uK[22];
    float r96 = r93 - r94;
    float r97 = r0 - r94;
    float r98 = dv(r96, r97);
    float r99 = r95 + (r0 - r95) * r98;
    float r100 = uK[23];
    float r101 = r76 - r12;
    float r102 = r0 - r12;
    float r103 = dv(r101, r102);
    float r104 = r100 + (r0 - r100) * r103;
    float r105 = r99 * r104;
    vec3 t106 = hsv(r11, r77, r105);
    float r106 = t106.x; float r107 = t106.y; float r108 = t106.z;
    float r109 = r12 * r12;
    float r110 = r12 * r12;

    fragColour = vec4(sat(r106), sat(r107), sat(r108), 1.0);
}
