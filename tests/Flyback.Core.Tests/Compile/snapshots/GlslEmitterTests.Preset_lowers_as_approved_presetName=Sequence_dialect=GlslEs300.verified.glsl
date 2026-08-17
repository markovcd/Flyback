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
    float r6 = uK[4];
    float r7 = uK[5];
    float r8 = uK[6];
    float r9 = uK[7];
    float r10 = uK[8];
    float r11 = uK[9];
    float r12 = clamp(r4, r0, max(r0, r4));
    float r13 = floor(r12);
    float r14 = r2 * r3;
    float r15 = md(r14, r13);
    float r16 = floor(r15);
    float r17 = uK[10];
    float r18 = step(r0, r16);
    float r19 = uK[11];
    float r20 = step(r19, r16);
    float r21 = step(r3, r16);
    float r22 = uK[12];
    float r23 = step(r22, r16);
    float r24 = uK[13];
    float r25 = step(r24, r16);
    float r26 = uK[14];
    float r27 = step(r26, r16);
    float r28 = uK[15];
    float r29 = step(r28, r16);
    float r30 = r0 - r18;
    float r31 = r7 * r30;
    float r32 = r0 * r30;
    float r33 = r18 - r20;
    float r34 = r8 * r33;
    float r35 = r0 * r33;
    float r36 = r31 + r34;
    float r37 = r32 + r35;
    float r38 = r20 - r21;
    float r39 = r9 * r38;
    float r40 = r0 * r38;
    float r41 = r36 + r39;
    float r42 = r37 + r40;
    float r43 = r21 - r23;
    float r44 = r10 * r43;
    float r45 = r0 * r43;
    float r46 = r41 + r44;
    float r47 = r42 + r45;
    float r48 = r23 - r25;
    float r49 = r11 * r48;
    float r50 = r0 * r48;
    float r51 = r46 + r49;
    float r52 = r47 + r50;
    float r53 = r25 - r27;
    float r54 = r10 * r53;
    float r55 = r0 * r53;
    float r56 = r51 + r54;
    float r57 = r52 + r55;
    float r58 = r27 - r29;
    float r59 = r9 * r58;
    float r60 = r0 * r58;
    float r61 = r56 + r59;
    float r62 = r57 + r60;
    float r63 = r29 - r17;
    float r64 = r8 * r63;
    float r65 = r0 * r63;
    float r66 = r61 + r64;
    float r67 = r62 + r65;
    float r68 = fr(r14);
    float r69 = uK[16];
    float r70 = clamp(r6, r69, max(r69, r0));
    float r71 = clamp(r5, r17, max(r17, r0));
    float r72 = sm(r17, r70, r68);
    float r73 = r71 - r70;
    float r74 = sm(r73, r71, r68);
    float r75 = r0 - r74;
    float r76 = r67 * r72;
    float r77 = r76 * r75;
    float r78 = dv(r16, r13);
    float r79 = uK[17];
    float r80 = px;
    float r81 = py;
    float r82 = sqrt(r80 * r80 + r81 * r81);
    float r83 = at2(r81, r80);
    float r84 = uK[18];
    float r85 = uK[19];
    float r86 = r78 - r17;
    float r87 = r0 - r17;
    float r88 = dv(r86, r87);
    float r89 = r84 + (r85 - r84) * r88;
    float r90 = sqrt(r80 * r80 + r81 * r81);
    float r91 = r90 * r89;
    float r92 = r91 + r17;
    float r93 = uK[20];
    float r94 = r92 * r93;
    float r95 = sin(r94);
    float r96 = uK[21];
    float r97 = uK[22];
    float r98 = r95 - r96;
    float r99 = r0 - r96;
    float r100 = dv(r98, r99);
    float r101 = r97 + (r0 - r97) * r100;
    float r102 = uK[23];
    float r103 = r77 - r17;
    float r104 = r0 - r17;
    float r105 = dv(r103, r104);
    float r106 = r102 + (r0 - r102) * r105;
    float r107 = r101 * r106;
    vec3 t108 = hsv(r78, r79, r107);
    float r108 = t108.x; float r109 = t108.y; float r110 = t108.z;

    fragColour = vec4(sat(r108), sat(r109), sat(r110), 1.0);
}
