#version 150

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
    float r32 = uK[11];
    float r33 = r18 * r32;
    float r34 = uK[12];
    float r35 = r33 + r34;
    float r36 = floor(r35);
    float r37 = uK[13];
    float r38 = r36 * r37;
    float r39 = r38 + r11;
    float r40 = r18 - r39;
    float r41 = abs(r40);
    float r42 = uK[14];
    float r43 = r33 + r42;
    float r44 = floor(r43);
    float r45 = r44 * r37;
    float r46 = r45 + r26;
    float r47 = r18 - r46;
    float r48 = abs(r47);
    float r49 = step(r48, r41);
    float r50 = r39 + (r46 - r39) * r49;
    float r51 = r41 + (r48 - r41) * r49;
    float r52 = uK[15];
    float r53 = r33 + r52;
    float r54 = floor(r53);
    float r55 = r54 * r37;
    float r56 = uK[16];
    float r57 = r55 + r56;
    float r58 = r18 - r57;
    float r59 = abs(r58);
    float r60 = step(r59, r51);
    float r61 = r50 + (r57 - r50) * r60;
    float r62 = r51 + (r59 - r51) * r60;
    float r63 = uK[17];
    float r64 = r33 + r63;
    float r65 = floor(r64);
    float r66 = r65 * r37;
    float r67 = uK[18];
    float r68 = r66 + r67;
    float r69 = r18 - r68;
    float r70 = abs(r69);
    float r71 = step(r70, r62);
    float r72 = r61 + (r68 - r61) * r71;
    float r73 = r62 + (r70 - r62) * r71;
    float r74 = uK[19];
    float r75 = r33 + r74;
    float r76 = floor(r75);
    float r77 = r76 * r37;
    float r78 = uK[20];
    float r79 = r77 + r78;
    float r80 = r18 - r79;
    float r81 = abs(r80);
    float r82 = step(r81, r73);
    float r83 = r72 + (r79 - r72) * r82;
    float r84 = r73 + (r81 - r73) * r82;
    float r85 = 0.0;
    float r86 = 0.0;
    float r87 = uK[21];
    float r88 = r86 * r87;
    float r89 = 0.0;
    float r90 = step(r34, r31);
    float r91 = r12 - r89;
    float r92 = r90 * r91;
    float r93 = r12 - r90;
    float r94 = max(r92, r93);
    float r95 = r12 - r85;
    float r96 = max(r94, r95);
    float r97 = r88 + (r83 - r88) * r96;
    float r98 = uK[22];
    float r99 = r97 * r98;
    float r100 = r97 * r32;
    float r101 = fr(r100);
    float r102 = uK[23];
    float r103 = uK[24];
    float r104 = r101 - r11;
    float r105 = r12 - r11;
    float r106 = dv(r104, r105);
    float r107 = r102 + (r103 - r102) * r106;
    float r108 = uK[25];
    float r109 = uK[26];
    float r110 = r10 - r11;
    float r111 = r12 - r11;
    float r112 = dv(r110, r111);
    float r113 = r108 + (r109 - r108) * r112;
    vec3 t114 = hsv(r107, r103, r113);
    float r114 = t114.x; float r115 = t114.y; float r116 = t114.z;
    float r117 = r11 * r11;
    float r118 = r11 * r11;

    fragColor = vec4(sat(r114), sat(r115), sat(r116), 1.0);
}
