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
    float r4 = uAspect;
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
    float r23 = uK[8];
    float r24 = r5 * r22 + r12;
    float r25 = fr(r24);
    float r26 = step(r23, r25);
    float r27 = uK[9];
    float r28 = r26 * r27;
    float r29 = uK[10];
    float r30 = r28 + r29;
    float r31 = r30 * r13;
    float r32 = r31 + r12;
    float r33 = uK[11];
    float r34 = r19 * r33;
    float r35 = uK[12];
    float r36 = r34 + r35;
    float r37 = floor(r36);
    float r38 = uK[13];
    float r39 = r37 * r38;
    float r40 = r39 + r12;
    float r41 = r19 - r40;
    float r42 = abs(r41);
    float r43 = uK[14];
    float r44 = r34 + r43;
    float r45 = floor(r44);
    float r46 = r45 * r38;
    float r47 = r46 + r27;
    float r48 = r19 - r47;
    float r49 = abs(r48);
    float r50 = step(r49, r42);
    float r51 = r40 + (r47 - r40) * r50;
    float r52 = r42 + (r49 - r42) * r50;
    float r53 = uK[15];
    float r54 = r34 + r53;
    float r55 = floor(r54);
    float r56 = r55 * r38;
    float r57 = uK[16];
    float r58 = r56 + r57;
    float r59 = r19 - r58;
    float r60 = abs(r59);
    float r61 = step(r60, r52);
    float r62 = r51 + (r58 - r51) * r61;
    float r63 = r52 + (r60 - r52) * r61;
    float r64 = uK[17];
    float r65 = r34 + r64;
    float r66 = floor(r65);
    float r67 = r66 * r38;
    float r68 = uK[18];
    float r69 = r67 + r68;
    float r70 = r19 - r69;
    float r71 = abs(r70);
    float r72 = step(r71, r63);
    float r73 = r62 + (r69 - r62) * r72;
    float r74 = r63 + (r71 - r63) * r72;
    float r75 = uK[19];
    float r76 = r34 + r75;
    float r77 = floor(r76);
    float r78 = r77 * r38;
    float r79 = uK[20];
    float r80 = r78 + r79;
    float r81 = r19 - r80;
    float r82 = abs(r81);
    float r83 = step(r82, r74);
    float r84 = r73 + (r80 - r73) * r83;
    float r85 = r74 + (r82 - r74) * r83;
    float r86 = 0.0;
    float r87 = 0.0;
    float r88 = uK[21];
    float r89 = r87 * r88;
    float r90 = 0.0;
    float r91 = step(r35, r32);
    float r92 = r13 - r90;
    float r93 = r91 * r92;
    float r94 = r13 - r91;
    float r95 = max(r93, r94);
    float r96 = r13 - r86;
    float r97 = max(r95, r96);
    float r98 = r89 + (r84 - r89) * r97;
    float r99 = uK[22];
    float r100 = r98 * r99;
    float r101 = r98 * r33;
    float r102 = fr(r101);
    float r103 = uK[23];
    float r104 = uK[24];
    float r105 = r102 - r12;
    float r106 = r13 - r12;
    float r107 = dv(r105, r106);
    float r108 = r103 + (r104 - r103) * r107;
    float r109 = uK[25];
    float r110 = uK[26];
    float r111 = r11 - r12;
    float r112 = r13 - r12;
    float r113 = dv(r111, r112);
    float r114 = r109 + (r110 - r109) * r113;
    vec3 t115 = hsv(r108, r104, r114);
    float r115 = t115.x; float r116 = t115.y; float r117 = t115.z;
    float r118 = r12 * r12;
    float r119 = r12 * r12;

    fragColor = vec4(sat(r115), sat(r116), sat(r117), 1.0);
}
