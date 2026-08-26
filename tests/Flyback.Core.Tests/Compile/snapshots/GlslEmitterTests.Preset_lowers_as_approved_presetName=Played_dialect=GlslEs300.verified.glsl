#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[17];
uniform float uLive[4];

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

    float r0 = uLive[0];
    float r1 = uLive[1];
    float r2 = uLive[2];
    float r3 = uLive[3];
    float r4 = 0.0;
    float r5 = r3 - r4;
    float r6 = abs(r5);
    float r7 = uK[0];
    float r8 = step(r7, r6);
    float r9 = 0.0;
    float r10 = uK[1];
    float r11 = r8 * r9;
    float r12 = r10 - r11;
    float r13 = r1 * r12;
    float r14 = uK[2];
    float r15 = uK[3];
    float r16 = clamp(r0, r14, max(r14, r15));
    float r17 = uK[4];
    float r18 = uK[5];
    float r19 = r16 - r14;
    float r20 = r15 - r14;
    float r21 = dv(r19, r20);
    float r22 = r17 + (r18 - r17) * r21;
    float r23 = uK[6];
    float r24 = px;
    float r25 = py;
    float r26 = sqrt(r24 * r24 + r25 * r25);
    float r27 = at2(r25, r24);
    float r28 = uK[7];
    float r29 = uK[8];
    float r30 = r16 - r14;
    float r31 = r15 - r14;
    float r32 = dv(r30, r31);
    float r33 = r28 + (r29 - r28) * r32;
    float r34 = sqrt(r24 * r24 + r25 * r25);
    float r35 = r34 * r33;
    float r36 = r35 + r18;
    float r37 = uK[9];
    float r38 = r36 * r37;
    float r39 = sin(r38);
    float r40 = uK[10];
    float r41 = uK[11];
    float r42 = r39 - r40;
    float r43 = r10 - r40;
    float r44 = dv(r42, r43);
    float r45 = r41 + (r10 - r41) * r44;
    float r46 = uK[12];
    float r47 = uK[13];
    float r48 = uTime;
    float r49 = 0.0;
    float r50 = r48 - r49;
    float r51 = uK[14];
    float r52 = step(r7, r13);
    float r53 = clamp(r7, r18, max(r18, r10));
    float r54 = uK[15];
    float r55 = pw(r54, r46);
    float r56 = max(r55, r51);
    float r57 = pw(r54, r47);
    float r58 = max(r57, r51);
    float r59 = pw(r54, r40);
    float r60 = max(r59, r51);
    float r61 = 0.0;
    float r62 = 0.0;
    float r63 = step(r10, r61);
    float r64 = max(r62, r63);
    float r65 = r52 * r64;
    float r66 = r10 - r65;
    float r67 = r52 * r66;
    float r68 = dv(r50, r56);
    float r69 = r10 - r53;
    float r70 = r69 * r50;
    float r71 = dv(r70, r58);
    float r72 = dv(r50, r60);
    float r73 = r61 + r68;
    float r74 = min(r73, r10);
    float r75 = r61 - r71;
    float r76 = max(r75, r53);
    float r77 = r61 - r72;
    float r78 = max(r77, r18);
    float r79 = r76 + (r74 - r76) * r67;
    float r80 = r78 + (r79 - r78) * r52;
    float r81 = clamp(r13, r18, max(r18, r10));
    float r82 = r81 + (r80 - r81) * r9;
    float r83 = uK[16];
    float r84 = r82 - r18;
    float r85 = r10 - r18;
    float r86 = dv(r84, r85);
    float r87 = r83 + (r10 - r83) * r86;
    float r88 = r45 * r87;
    vec3 t89 = hsv(r22, r23, r88);
    float r89 = t89.x; float r90 = t89.y; float r91 = t89.z;
    float r92 = r18 * r18;
    float r93 = r18 * r18;

    fragColor = vec4(sat(r89), sat(r90), sat(r91), 1.0);
}
