#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[24];

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

    float r0 = uTime;
    float r1 = uK[0];
    float r2 = uK[1];
    float r3 = uK[2];
    float r4 = r0 * r1;
    float r5 = uK[3];
    float r6 = md(r4, r5);
    float r7 = floor(r6);
    float r8 = fr(r4);
    float r9 = dv(r7, r5);
    float r10 = uK[4];
    float r11 = uK[5];
    float r12 = step(r10, r7);
    float r13 = uK[6];
    float r14 = step(r13, r7);
    float r15 = step(r1, r7);
    float r16 = uK[7];
    float r17 = step(r16, r7);
    float r18 = uK[8];
    float r19 = step(r18, r7);
    float r20 = uK[9];
    float r21 = step(r20, r7);
    float r22 = uK[10];
    float r23 = step(r22, r7);
    float r24 = r10 - r12;
    float r25 = uK[11];
    float r26 = r24 * r25;
    float r27 = r24 * r10;
    float r28 = r12 - r14;
    float r29 = uK[12];
    float r30 = r28 * r29;
    float r31 = r28 * r10;
    float r32 = r26 + r30;
    float r33 = r27 + r31;
    float r34 = r14 - r15;
    float r35 = uK[13];
    float r36 = r34 * r35;
    float r37 = r34 * r10;
    float r38 = r32 + r36;
    float r39 = r33 + r37;
    float r40 = r15 - r17;
    float r41 = uK[14];
    float r42 = r40 * r41;
    float r43 = r40 * r10;
    float r44 = r38 + r42;
    float r45 = r39 + r43;
    float r46 = r17 - r19;
    float r47 = uK[15];
    float r48 = r46 * r47;
    float r49 = r46 * r10;
    float r50 = r44 + r48;
    float r51 = r45 + r49;
    float r52 = r19 - r21;
    float r53 = r52 * r41;
    float r54 = r52 * r10;
    float r55 = r50 + r53;
    float r56 = r51 + r54;
    float r57 = r21 - r23;
    float r58 = r57 * r35;
    float r59 = r57 * r10;
    float r60 = r55 + r58;
    float r61 = r56 + r59;
    float r62 = r23 - r11;
    float r63 = r62 * r29;
    float r64 = r62 * r10;
    float r65 = r60 + r63;
    float r66 = r61 + r64;
    float r67 = uK[16];
    float r68 = clamp(r3, r67, max(r67, r10));
    float r69 = clamp(r2, r11, max(r11, r10));
    float r70 = sm(r11, r68, r8);
    float r71 = r69 - r68;
    float r72 = sm(r71, r69, r8);
    float r73 = r10 - r72;
    float r74 = r66 * r70;
    float r75 = r74 * r73;
    float r76 = uK[17];
    float r77 = px;
    float r78 = py;
    float r79 = sqrt(r77 * r77 + r78 * r78);
    float r80 = at2(r78, r77);
    float r81 = uK[18];
    float r82 = uK[19];
    float r83 = r9 - r11;
    float r84 = r10 - r11;
    float r85 = dv(r83, r84);
    float r86 = r81 + (r82 - r81) * r85;
    float r87 = sqrt(r77 * r77 + r78 * r78);
    float r88 = r87 * r86;
    float r89 = r88 + r11;
    float r90 = uK[20];
    float r91 = r89 * r90;
    float r92 = sin(r91);
    float r93 = uK[21];
    float r94 = uK[22];
    float r95 = r92 - r93;
    float r96 = r10 - r93;
    float r97 = dv(r95, r96);
    float r98 = r94 + (r10 - r94) * r97;
    float r99 = uK[23];
    float r100 = r75 - r11;
    float r101 = r10 - r11;
    float r102 = dv(r100, r101);
    float r103 = r99 + (r10 - r99) * r102;
    float r104 = r98 * r103;
    vec3 t105 = hsv(r9, r76, r104);
    float r105 = t105.x; float r106 = t105.y; float r107 = t105.z;
    float r108 = r11 * r11;
    float r109 = r11 * r11;

    fragColor = vec4(sat(r105), sat(r106), sat(r107), 1.0);
}
