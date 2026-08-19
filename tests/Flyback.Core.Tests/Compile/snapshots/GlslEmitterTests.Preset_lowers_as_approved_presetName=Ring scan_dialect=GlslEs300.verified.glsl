#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[19];

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
    float r4 = r0 * r1 + r2;
    float r5 = uK[3];
    float r6 = r4 * r5;
    float r7 = sin(r6);
    float r8 = r7 * r3;
    float r9 = r8 + r2;
    float r10 = uK[4];
    float r11 = uK[5];
    float r12 = r9 - r10;
    float r13 = r3 - r10;
    float r14 = dv(r12, r13);
    float r15 = r1 + (r11 - r1) * r14;
    float r16 = uK[6];
    float r17 = px;
    float r18 = py;
    float r19 = sqrt(r17 * r17 + r18 * r18);
    float r20 = at2(r18, r17);
    float r21 = uK[7];
    float r22 = sqrt(r17 * r17 + r18 * r18);
    float r23 = r22 * r21;
    float r24 = r23 + r2;
    float r25 = r24 * r5;
    float r26 = sin(r25);
    float r27 = uK[8];
    float r28 = uK[9];
    float r29 = r26 - r10;
    float r30 = r3 - r10;
    float r31 = dv(r29, r30);
    float r32 = r27 + (r28 - r27) * r31;
    vec3 t33 = hsv(r15, r16, r32);
    float r33 = t33.x; float r34 = t33.y; float r35 = t33.z;
    float r36 = uK[10];
    float r37 = uK[11];
    float r38 = r17 - r15;
    float r39 = r18 - r2;
    float r40 = r0 * r36 + r2;
    float r41 = at2(r39, r38);
    float r42 = r40 * r5;
    float r43 = 0.0;
    float r44 = r41 + (r42 - r41) * r43;
    float r45 = cos(r44);
    float r46 = r37 * r45;
    float r47 = r15 + r46;
    float r48 = sin(r44);
    float r49 = r37 * r48;
    float r50 = r2 + r49;
    float r51 = sqrt(r47 * r47 + r50 * r50);
    float r52 = at2(r50, r47);
    float r53 = sqrt(r47 * r47 + r50 * r50);
    float r54 = r53 * r21;
    float r55 = r54 + r2;
    float r56 = r55 * r5;
    float r57 = sin(r56);
    float r58 = sqrt(r38 * r38 + r39 * r39);
    float r59 = dv(r57, r3);
    float r60 = r59 * r37;
    float r61 = uK[12];
    float r62 = r60 * r61;
    float r63 = r37 + r62;
    float r64 = uK[13];
    float r65 = uK[14];
    float r66 = r58 - r63;
    float r67 = abs(r66);
    float r68 = sm(r64, r65, r67);
    float r69 = r3 - r68;
    float r70 = uK[15];
    float r71 = r58 - r37;
    float r72 = abs(r71);
    float r73 = sm(r70, r64, r72);
    float r74 = r3 - r73;
    float r75 = uK[16];
    float r76 = uK[17];
    float r77 = r75;
    float r78 = r3;
    float r79 = r76;
    float r80 = uK[18];
    float r81 = r74 * r80;
    float r82 = r69 + r81;
    float r83 = r77 * r82;
    float r84 = r78 * r82;
    float r85 = r79 * r82;
    float r86 = r33 + r83;
    float r87 = r34 + r84;
    float r88 = r35 + r85;
    float r89 = r2 * r2;
    float r90 = r2 * r2;

    fragColor = vec4(sat(r86), sat(r87), sat(r88), 1.0);
}
