#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[19];

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
    float r5 = r2 * r3 + r4;
    float r6 = uK[3];
    float r7 = r5 * r6;
    float r8 = sin(r7);
    float r9 = r8 * r0;
    float r10 = r9 + r4;
    float r11 = uK[4];
    float r12 = uK[5];
    float r13 = r10 - r11;
    float r14 = r0 - r11;
    float r15 = dv(r13, r14);
    float r16 = r3 + (r12 - r3) * r15;
    float r17 = uK[6];
    float r18 = px;
    float r19 = py;
    float r20 = sqrt(r18 * r18 + r19 * r19);
    float r21 = at2(r19, r18);
    float r22 = uK[7];
    float r23 = sqrt(r18 * r18 + r19 * r19);
    float r24 = r23 * r22;
    float r25 = r24 + r4;
    float r26 = r25 * r6;
    float r27 = sin(r26);
    float r28 = uK[8];
    float r29 = uK[9];
    float r30 = r27 - r11;
    float r31 = r0 - r11;
    float r32 = dv(r30, r31);
    float r33 = r28 + (r29 - r28) * r32;
    vec3 t34 = hsv(r16, r17, r33);
    float r34 = t34.x; float r35 = t34.y; float r36 = t34.z;
    float r37 = uK[10];
    float r38 = uK[11];
    float r39 = r18 - r16;
    float r40 = r19 - r4;
    float r41 = r2 * r37 + r4;
    float r42 = at2(r40, r39);
    float r43 = r41 * r6;
    float r44 = 0.0;
    float r45 = r42 + (r43 - r42) * r44;
    float r46 = cos(r45);
    float r47 = r38 * r46;
    float r48 = r16 + r47;
    float r49 = sin(r45);
    float r50 = r38 * r49;
    float r51 = r4 + r50;
    float r52 = sqrt(r48 * r48 + r51 * r51);
    float r53 = at2(r51, r48);
    float r54 = sqrt(r48 * r48 + r51 * r51);
    float r55 = r54 * r22;
    float r56 = r55 + r4;
    float r57 = r56 * r6;
    float r58 = sin(r57);
    float r59 = sqrt(r39 * r39 + r40 * r40);
    float r60 = dv(r58, r0);
    float r61 = r60 * r38;
    float r62 = uK[12];
    float r63 = r61 * r62;
    float r64 = r38 + r63;
    float r65 = uK[13];
    float r66 = uK[14];
    float r67 = r59 - r64;
    float r68 = abs(r67);
    float r69 = sm(r65, r66, r68);
    float r70 = r0 - r69;
    float r71 = uK[15];
    float r72 = r59 - r38;
    float r73 = abs(r72);
    float r74 = sm(r71, r65, r73);
    float r75 = r0 - r74;
    float r76 = uK[16];
    float r77 = uK[17];
    float r78 = r76;
    float r79 = r0;
    float r80 = r77;
    float r81 = uK[18];
    float r82 = r75 * r81;
    float r83 = r70 + r82;
    float r84 = r78 * r83;
    float r85 = r79 * r83;
    float r86 = r80 * r83;
    float r87 = r34 + r84;
    float r88 = r35 + r85;
    float r89 = r36 + r86;
    float r90 = r4 * r4;
    float r91 = r4 * r4;

    fragColour = vec4(sat(r87), sat(r88), sat(r89), 1.0);
}
