#version 150

uniform float uTime;
uniform float uAspect;
uniform float uK[21];

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
    float r2 = uK[1];
    float r3 = px;
    float r4 = py;
    float r5 = uAspect;
    float r6 = r3 + r5;
    float r7 = uK[2];
    float r8 = r5 * r7;
    float r9 = dv(r6, r8);
    float r10 = uK[3];
    float r11 = r9 * r10;
    float r12 = 0.0;
    float r13 = dv(r12, r2);
    float r14 = uK[4];
    float r15 = r5 * r14;
    float r16 = uK[5];
    float r17 = r9 * r16;
    float r18 = uK[6];
    float r19 = r17 + r18;
    float r20 = uK[7];
    float r21 = uK[8];
    float r22 = r4 - r13;
    float r23 = abs(r22);
    float r24 = sm(r20, r21, r23);
    float r25 = r10 - r24;
    float r26 = min(r13, r0);
    float r27 = step(r26, r4);
    float r28 = max(r13, r0);
    float r29 = step(r4, r28);
    float r30 = r27 * r29;
    float r31 = abs(r13);
    float r32 = step(r10, r31);
    float r33 = uK[9];
    float r34 = abs(r4);
    float r35 = step(r33, r34);
    float r36 = r13 * r4;
    float r37 = step(r0, r36);
    float r38 = r32 * r35;
    float r39 = r38 * r37;
    float r40 = uK[10];
    float r41 = r30 * r40;
    float r42 = r41 + r25;
    float r43 = r42 * r19;
    float r44 = dv(r3, r15);
    float r45 = uK[11];
    float r46 = r44 + r45;
    float r47 = fr(r46);
    float r48 = uK[12];
    float r49 = r47 + r48;
    float r50 = abs(r49);
    float r51 = r50 * r15;
    float r52 = uK[13];
    float r53 = uK[14];
    float r54 = sm(r52, r53, r51);
    float r55 = r10 - r54;
    float r56 = dv(r4, r14);
    float r57 = r56 + r45;
    float r58 = fr(r57);
    float r59 = r58 + r48;
    float r60 = abs(r59);
    float r61 = r60 * r14;
    float r62 = sm(r52, r53, r61);
    float r63 = r10 - r62;
    float r64 = r55 + r63;
    float r65 = uK[15];
    float r66 = uK[16];
    float r67 = abs(r4);
    float r68 = sm(r65, r66, r67);
    float r69 = r10 - r68;
    float r70 = uK[17];
    float r71 = r64 * r70;
    float r72 = uK[18];
    float r73 = r69 * r72;
    float r74 = r71 + r73;
    float r75 = r74 + r43;
    float r76 = uK[19];
    float r77 = uK[20];
    float r78 = r76;
    float r79 = r10;
    float r80 = r77;
    float r81 = r78 * r75;
    float r82 = r79 * r75;
    float r83 = r80 * r75;
    float r84 = r10;
    float r85 = r14;
    float r86 = r72;
    float r87 = r84 * r39;
    float r88 = r85 * r39;
    float r89 = r86 * r39;
    float r90 = r81 + r87;
    float r91 = r82 + r88;
    float r92 = r83 + r89;

    fragColor = vec4(sat(r90), sat(r91), sat(r92), 1.0);
}
