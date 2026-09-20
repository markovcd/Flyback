#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
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
    float r5 = uTime;
    float r6 = uK[0];
    float r7 = r5 * r6;
    float r8 = uK[1];
    float r9 = r0 * r8;
    float r10 = r1 * r8;
    float r11 = nz(r9, r10, r7);
    float r12 = uK[2];
    float r13 = uK[3];
    float r14 = uK[4];
    float r15 = uK[5];
    float r16 = r11 - r12;
    float r17 = r13 - r12;
    float r18 = dv(r16, r17);
    float r19 = r14 + (r15 - r14) * r18;
    float r20 = uK[6];
    float r21 = uK[7];
    float r22 = r20 * r21;
    float r24 = uK[8];
    float r25 = r5 * r22 + r12;
    float r26 = fr(r25);
    float r27 = step(r24, r26);
    float r28 = uK[9];
    float r29 = r27 * r28;
    float r30 = uK[10];
    float r31 = r29 + r30;
    float r32 = r31 * r13;
    float r33 = r32 + r12;
    float r34 = uK[11];
    float r35 = r19 * r34;
    float r36 = uK[12];
    float r37 = r35 + r36;
    float r38 = floor(r37);
    float r39 = uK[13];
    float r40 = r38 * r39;
    float r41 = r40 + r12;
    float r42 = r19 - r41;
    float r43 = abs(r42);
    float r44 = uK[14];
    float r45 = r35 + r44;
    float r46 = floor(r45);
    float r47 = r46 * r39;
    float r48 = r47 + r28;
    float r49 = r19 - r48;
    float r50 = abs(r49);
    float r51 = step(r50, r43);
    float r52 = r41 + (r48 - r41) * r51;
    float r53 = r43 + (r50 - r43) * r51;
    float r54 = uK[15];
    float r55 = r35 + r54;
    float r56 = floor(r55);
    float r57 = r56 * r39;
    float r58 = uK[16];
    float r59 = r57 + r58;
    float r60 = r19 - r59;
    float r61 = abs(r60);
    float r62 = step(r61, r53);
    float r63 = r52 + (r59 - r52) * r62;
    float r64 = r53 + (r61 - r53) * r62;
    float r65 = uK[17];
    float r66 = r35 + r65;
    float r67 = floor(r66);
    float r68 = r67 * r39;
    float r69 = uK[18];
    float r70 = r68 + r69;
    float r71 = r19 - r70;
    float r72 = abs(r71);
    float r73 = step(r72, r64);
    float r74 = r63 + (r70 - r63) * r73;
    float r75 = r64 + (r72 - r64) * r73;
    float r76 = uK[19];
    float r77 = r35 + r76;
    float r78 = floor(r77);
    float r79 = r78 * r39;
    float r80 = uK[20];
    float r81 = r79 + r80;
    float r82 = r19 - r81;
    float r83 = abs(r82);
    float r84 = step(r83, r75);
    float r85 = r74 + (r81 - r74) * r84;
    float r87 = 0.0;
    float r88 = 0.0;
    float r89 = uK[21];
    float r90 = r88 * r89;
    float r91 = 0.0;
    float r92 = step(r36, r33);
    float r93 = r13 - r91;
    float r94 = r92 * r93;
    float r95 = r13 - r92;
    float r96 = max(r94, r95);
    float r97 = r13 - r87;
    float r98 = max(r96, r97);
    float r99 = r90 + (r85 - r90) * r98;
    float r100 = uK[22];
    float r101 = r99 * r100;
    float r102 = r99 * r34;
    float r103 = fr(r102);
    float r104 = uK[23];
    float r105 = uK[24];
    float r106 = r103 - r12;
    float r107 = r13 - r12;
    float r108 = dv(r106, r107);
    float r109 = r104 + (r105 - r104) * r108;
    float r110 = uK[25];
    float r111 = uK[26];
    float r112 = r11 - r12;
    float r113 = r13 - r12;
    float r114 = dv(r112, r113);
    float r115 = r110 + (r111 - r110) * r114;
    vec3 t116 = hsv(r109, r105, r115);
    float r116 = t116.x; float r117 = t116.y; float r118 = t116.z;

    fragColor = vec4(sat(r116), sat(r117), sat(r118), 1.0);
}
