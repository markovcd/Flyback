#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[23];

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
    float r6 = uK[1];
    float r7 = r5 * r6;
    float r8 = r4 * r7;
    float r9 = floor(r8);
    float r10 = uK[2];
    float r11 = r9 * r10;
    float r12 = uK[3];
    float r13 = r0 * r12;
    float r14 = r1 * r12;
    float r15 = nz(r13, r14, r11);
    float r16 = uK[4];
    float r17 = uK[5];
    float r18 = uK[6];
    float r19 = uK[7];
    float r20 = r15 - r16;
    float r21 = r17 - r16;
    float r22 = dv(r20, r21);
    float r23 = r18 + (r19 - r18) * r22;
    float r24 = uK[8];
    float r25 = r23 * r24;
    float r26 = uK[9];
    float r27 = r25 + r26;
    float r28 = floor(r27);
    float r29 = uK[10];
    float r30 = r28 * r29;
    float r31 = r30 + r16;
    float r32 = r23 - r31;
    float r33 = abs(r32);
    float r34 = uK[11];
    float r35 = r25 + r34;
    float r36 = floor(r35);
    float r37 = r36 * r29;
    float r38 = uK[12];
    float r39 = r37 + r38;
    float r40 = r23 - r39;
    float r41 = abs(r40);
    float r42 = step(r41, r33);
    float r43 = r31 + (r39 - r31) * r42;
    float r44 = r33 + (r41 - r33) * r42;
    float r45 = uK[13];
    float r46 = r25 + r45;
    float r47 = floor(r46);
    float r48 = r47 * r29;
    float r49 = uK[14];
    float r50 = r48 + r49;
    float r51 = r23 - r50;
    float r52 = abs(r51);
    float r53 = step(r52, r44);
    float r54 = r43 + (r50 - r43) * r53;
    float r55 = r44 + (r52 - r44) * r53;
    float r56 = uK[15];
    float r57 = r25 + r56;
    float r58 = floor(r57);
    float r59 = r58 * r29;
    float r60 = uK[16];
    float r61 = r59 + r60;
    float r62 = r23 - r61;
    float r63 = abs(r62);
    float r64 = step(r63, r55);
    float r65 = r54 + (r61 - r54) * r64;
    float r66 = r55 + (r63 - r55) * r64;
    float r67 = uK[17];
    float r68 = r25 + r67;
    float r69 = floor(r68);
    float r70 = r69 * r29;
    float r71 = uK[18];
    float r72 = r70 + r71;
    float r73 = r23 - r72;
    float r74 = abs(r73);
    float r75 = step(r74, r66);
    float r76 = r65 + (r72 - r65) * r75;
    float r77 = r66 + (r74 - r66) * r75;
    float r78 = r76 * r24;
    float r79 = fr(r78);
    float r80 = uK[19];
    float r81 = uK[20];
    float r82 = r79 - r16;
    float r83 = r17 - r16;
    float r84 = dv(r82, r83);
    float r85 = r80 + (r81 - r80) * r84;
    float r86 = uK[21];
    float r87 = uK[22];
    float r88 = r15 - r16;
    float r89 = r17 - r16;
    float r90 = dv(r88, r89);
    float r91 = r86 + (r87 - r86) * r90;
    vec3 t92 = hsv(r85, r81, r91);
    float r92 = t92.x; float r93 = t92.y; float r94 = t92.z;
    float r95 = r16 * r16;
    float r96 = r16 * r16;

    fragColor = vec4(sat(r92), sat(r93), sat(r94), 1.0);
}
