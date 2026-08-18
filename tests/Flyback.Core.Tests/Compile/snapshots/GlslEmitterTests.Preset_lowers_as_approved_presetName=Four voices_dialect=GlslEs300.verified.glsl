#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[17];

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
    float r17 = r16 * r1;
    float r18 = uK[4];
    float r19 = r17 * r18 + r0;
    float r20 = r19 * r8;
    float r21 = sin(r20);
    float r22 = r21 * r6;
    float r23 = r22 + r6;
    float r24 = uK[5];
    float r25 = uK[6];
    float r26 = r4 * r25 + r0;
    float r27 = r26 * r8;
    float r28 = sin(r27);
    float r29 = r28 * r6;
    float r30 = r29 + r6;
    vec3 t31 = hsv(r24, r1, r30);
    float r31 = t31.x; float r32 = t31.y; float r33 = t31.z;
    float r34 = uK[7];
    float r35 = uK[8];
    float r36 = r17 * r34 + r35;
    float r37 = r36 * r8;
    float r38 = sin(r37);
    float r39 = r38 * r6;
    float r40 = r39 + r6;
    float r41 = uK[9];
    float r42 = uK[10];
    float r43 = r4 * r42 + r0;
    float r44 = r43 * r8;
    float r45 = sin(r44);
    float r46 = r45 * r6;
    float r47 = r46 + r6;
    vec3 t48 = hsv(r41, r1, r47);
    float r48 = t48.x; float r49 = t48.y; float r50 = t48.z;
    float r51 = uK[11];
    float r52 = uK[12];
    float r53 = r17 * r51 + r52;
    float r54 = r53 * r8;
    float r55 = sin(r54);
    float r56 = r55 * r6;
    float r57 = r56 + r6;
    float r58 = uK[13];
    float r59 = uK[14];
    float r60 = r4 * r59 + r0;
    float r61 = r60 * r8;
    float r62 = sin(r61);
    float r63 = r62 * r6;
    float r64 = r63 + r6;
    vec3 t65 = hsv(r58, r1, r64);
    float r65 = t65.x; float r66 = t65.y; float r67 = t65.z;
    float r68 = uK[15];
    float r69 = uK[16];
    float r70 = r17 * r68 + r69;
    float r71 = r70 * r8;
    float r72 = sin(r71);
    float r73 = r72 * r6;
    float r74 = r73 + r6;
    float r75 = r13 * r23;
    float r76 = r14 * r23;
    float r77 = r15 * r23;
    float r78 = r31 * r40;
    float r79 = r32 * r40;
    float r80 = r33 * r40;
    float r81 = r75 + r78;
    float r82 = r76 + r79;
    float r83 = r77 + r80;
    float r84 = r48 * r57;
    float r85 = r49 * r57;
    float r86 = r50 * r57;
    float r87 = r81 + r84;
    float r88 = r82 + r85;
    float r89 = r83 + r86;
    float r90 = r65 * r74;
    float r91 = r66 * r74;
    float r92 = r67 * r74;
    float r93 = r87 + r90;
    float r94 = r88 + r91;
    float r95 = r89 + r92;
    float r96 = r93 * r52;
    float r97 = r94 * r52;
    float r98 = r95 * r52;
    float r99 = r96 + r0;
    float r100 = r97 + r0;
    float r101 = r98 + r0;
    float r102 = r0 * r0;
    float r103 = r0 * r0;

    fragColour = vec4(sat(r99), sat(r100), sat(r101), 1.0);
}
