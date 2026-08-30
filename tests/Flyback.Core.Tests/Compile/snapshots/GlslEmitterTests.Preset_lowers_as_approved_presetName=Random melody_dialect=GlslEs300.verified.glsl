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
    float r2 = sqrt(r0 * r0 + r1 * r1);
    float r3 = at2(r1, r0);
    float r4 = uTime;
    float r5 = uK[0];
    float r6 = r4 * r5;
    float r7 = uK[1];
    float r8 = r0 * r7;
    float r9 = r1 * r7;
    float r10 = nz(r8, r9, r6);
    float r11 = uK[2];
    float r12 = uK[3];
    float r13 = uK[4];
    float r14 = uK[5];
    float r15 = r10 - r11;
    float r16 = r12 - r11;
    float r17 = dv(r15, r16);
    float r18 = r13 + (r14 - r13) * r17;
    float r19 = uK[6];
    float r20 = uK[7];
    float r21 = r19 * r20;
    float r22 = uK[8];
    float r23 = r4 * r21 + r11;
    float r24 = fr(r23);
    float r25 = step(r22, r24);
    float r26 = uK[9];
    float r27 = r25 * r26;
    float r28 = uK[10];
    float r29 = r27 + r28;
    float r30 = r29 * r12;
    float r31 = r30 + r11;
    float r32 = 0.0;
    float r33 = 0.0;
    float r34 = uK[11];
    float r35 = r33 * r34;
    float r36 = 0.0;
    float r37 = uK[12];
    float r38 = step(r37, r31);
    float r39 = r12 - r36;
    float r40 = r38 * r39;
    float r41 = r12 - r32;
    float r42 = max(r40, r41);
    float r43 = r35 + (r18 - r35) * r42;
    float r44 = uK[13];
    float r45 = r43 * r44;
    float r46 = uK[14];
    float r47 = r43 * r46;
    float r48 = r47 + r37;
    float r49 = floor(r48);
    float r50 = uK[15];
    float r51 = r49 * r50;
    float r52 = r51 + r11;
    float r53 = r43 - r52;
    float r54 = abs(r53);
    float r55 = uK[16];
    float r56 = r47 + r55;
    float r57 = floor(r56);
    float r58 = r57 * r50;
    float r59 = r58 + r26;
    float r60 = r43 - r59;
    float r61 = abs(r60);
    float r62 = step(r61, r54);
    float r63 = r52 + (r59 - r52) * r62;
    float r64 = r54 + (r61 - r54) * r62;
    float r65 = uK[17];
    float r66 = r47 + r65;
    float r67 = floor(r66);
    float r68 = r67 * r50;
    float r69 = uK[18];
    float r70 = r68 + r69;
    float r71 = r43 - r70;
    float r72 = abs(r71);
    float r73 = step(r72, r64);
    float r74 = r63 + (r70 - r63) * r73;
    float r75 = r64 + (r72 - r64) * r73;
    float r76 = uK[19];
    float r77 = r47 + r76;
    float r78 = floor(r77);
    float r79 = r78 * r50;
    float r80 = uK[20];
    float r81 = r79 + r80;
    float r82 = r43 - r81;
    float r83 = abs(r82);
    float r84 = step(r83, r75);
    float r85 = r74 + (r81 - r74) * r84;
    float r86 = r75 + (r83 - r75) * r84;
    float r87 = uK[21];
    float r88 = r47 + r87;
    float r89 = floor(r88);
    float r90 = r89 * r50;
    float r91 = uK[22];
    float r92 = r90 + r91;
    float r93 = r43 - r92;
    float r94 = abs(r93);
    float r95 = step(r94, r86);
    float r96 = r85 + (r92 - r85) * r95;
    float r97 = r86 + (r94 - r86) * r95;
    float r98 = uK[23];
    float r99 = r47 + r98;
    float r100 = floor(r99);
    float r101 = r100 * r50;
    float r102 = uK[24];
    float r103 = r101 + r102;
    float r104 = r43 - r103;
    float r105 = abs(r104);
    float r106 = step(r105, r97);
    float r107 = r96 + (r103 - r96) * r106;
    float r108 = r97 + (r105 - r97) * r106;
    float r109 = 0.0;
    float r110 = r109 * r34;
    float r111 = 0.0;
    float r112 = step(r37, r11);
    float r113 = r12 - r111;
    float r114 = r112 * r113;
    float r115 = r12 - r112;
    float r116 = max(r114, r115);
    float r117 = r12 - r32;
    float r118 = max(r116, r117);
    float r119 = r110 + (r107 - r110) * r118;
    float r120 = r119 * r44;
    float r121 = r119 * r46;
    float r122 = fr(r121);
    float r123 = uK[25];
    float r124 = uK[26];
    float r125 = r10 - r11;
    float r126 = r12 - r11;
    float r127 = dv(r125, r126);
    float r128 = r22 + (r124 - r22) * r127;
    vec3 t129 = hsv(r122, r123, r128);
    float r129 = t129.x; float r130 = t129.y; float r131 = t129.z;
    float r132 = r11 * r11;
    float r133 = r11 * r11;

    fragColor = vec4(sat(r129), sat(r130), sat(r131), 1.0);
}
