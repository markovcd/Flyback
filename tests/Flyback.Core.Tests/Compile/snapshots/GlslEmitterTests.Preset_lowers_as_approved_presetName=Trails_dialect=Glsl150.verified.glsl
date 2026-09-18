#version 150

uniform float uTime;
uniform float uAspect;
uniform float uK[13];

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

uniform sampler2D uPrevious;
uniform float uFeedbackScaleX;
uniform float uFeedbackScaleY;

vec3 fb(float u, float v)
{
    u = fin(u) ? u : -uAspect;
    v = fin(v) ? v :  1.0;

    return texture(uPrevious, vec2(u * uFeedbackScaleX + 0.5, v * uFeedbackScaleY + 0.5)).rgb;
}

void main()
{
    float px = (vUv.x * 2.0 - 1.0) * uAspect;
    float py = vUv.y * 2.0 - 1.0;

    float r0 = uTime;
    float r1 = uK[0];
    float r2 = r0 * r1;
    float r3 = uK[1];
    float r4 = uK[2];
    float r5 = uK[3];
    float r6 = px;
    float r7 = py;
    float r11 = uK[4];
    float r12 = uK[5];
    float r13 = uK[6];
    float r14 = r0 * r11 + r12;
    float r15 = uK[7];
    float r16 = r14 * r15;
    float r17 = sin(r16);
    float r18 = r17 * r13;
    float r19 = r18 + r12;
    float r20 = r6 - r19;
    float r21 = uK[8];
    float r22 = uK[9];
    float r23 = r0 * r21 + r22;
    float r24 = r23 * r15;
    float r25 = sin(r24);
    float r26 = r25 * r11;
    float r27 = r26 + r12;
    float r28 = r7 - r27;
    float r29 = sqrt(r20 * r20 + r28 * r28);
    float r30 = sm(r4, r5, r29);
    vec3 t31 = hsv(r2, r3, r30);
    float r31 = t31.x; float r32 = t31.y; float r33 = t31.z;
    float r36 = uK[10];
    float r37 = uK[11];
    float r38 = uK[12];
    float r39 = r6 * r36;
    float r40 = r7 * r36;
    float r41 = cos(r37);
    float r42 = sin(r37);
    float r43 = r39 * r41;
    float r44 = r40 * r42;
    float r45 = r43 - r44;
    float r46 = r39 * r42;
    float r47 = r40 * r41;
    float r48 = r46 + r47;
    float r49 = r45 - r12;
    float r50 = r48 - r12;
    vec3 t51 = fb(r49, r50);
    float r51 = t51.x; float r52 = t51.y; float r53 = t51.z;
    float r54 = r51 * r38;
    float r55 = r52 * r38;
    float r56 = r53 * r38;
    float r57 = max(r54, r31);
    float r58 = max(r55, r32);
    float r59 = max(r56, r33);

    fragColor = vec4(sat(r57), sat(r58), sat(r59), 1.0);
}
