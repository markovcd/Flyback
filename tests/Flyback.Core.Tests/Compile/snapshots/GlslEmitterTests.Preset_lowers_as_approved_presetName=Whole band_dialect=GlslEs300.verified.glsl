#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform float uTime;
uniform float uAspect;
uniform float uK[68];

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

    float r0 = px;
    float r1 = py;
    float r5 = uK[0];
    float r6 = uTime;
    float r7 = uK[1];
    float r8 = uK[2];
    float r9 = r7 * r8;
    float r11 = uK[3];
    float r12 = r9 * r11;
    float r13 = uK[4];
    float r14 = uK[5];
    float r15 = r6 * r12;
    float r16 = uK[6];
    float r17 = md(r15, r16);
    float r18 = floor(r17);
    float r19 = fr(r15);
    float r21 = uK[7];
    float r22 = uK[8];
    float r23 = step(r21, r18);
    float r24 = uK[9];
    float r25 = step(r24, r18);
    float r26 = uK[10];
    float r27 = step(r26, r18);
    float r28 = step(r11, r18);
    float r29 = uK[11];
    float r30 = step(r29, r18);
    float r31 = uK[12];
    float r32 = step(r31, r18);
    float r33 = uK[13];
    float r34 = step(r33, r18);
    float r35 = uK[14];
    float r36 = step(r35, r18);
    float r37 = uK[15];
    float r38 = step(r37, r18);
    float r39 = uK[16];
    float r40 = step(r39, r18);
    float r41 = uK[17];
    float r42 = step(r41, r18);
    float r43 = uK[18];
    float r44 = step(r43, r18);
    float r45 = uK[19];
    float r46 = step(r45, r18);
    float r47 = uK[20];
    float r48 = step(r47, r18);
    float r49 = uK[21];
    float r50 = step(r49, r18);
    float r51 = r21 - r23;
    float r53 = r51 * r21;
    float r54 = r23 - r25;
    float r56 = r54 * r22;
    float r58 = r53 + r56;
    float r59 = r25 - r27;
    float r61 = r59 * r22;
    float r63 = r58 + r61;
    float r64 = r27 - r28;
    float r66 = r64 * r22;
    float r68 = r63 + r66;
    float r69 = r28 - r30;
    float r71 = uK[22];
    float r72 = r69 * r71;
    float r74 = r68 + r72;
    float r75 = r30 - r32;
    float r77 = r75 * r22;
    float r79 = r74 + r77;
    float r80 = r32 - r34;
    float r82 = uK[23];
    float r83 = r80 * r82;
    float r85 = r79 + r83;
    float r86 = r34 - r36;
    float r88 = r86 * r22;
    float r90 = r85 + r88;
    float r91 = r36 - r38;
    float r93 = uK[24];
    float r94 = r91 * r93;
    float r96 = r90 + r94;
    float r97 = r38 - r40;
    float r99 = r97 * r22;
    float r101 = r96 + r99;
    float r102 = r40 - r42;
    float r104 = r102 * r22;
    float r106 = r101 + r104;
    float r107 = r42 - r44;
    float r109 = r107 * r22;
    float r111 = r106 + r109;
    float r112 = r44 - r46;
    float r114 = uK[25];
    float r115 = r112 * r114;
    float r117 = r111 + r115;
    float r118 = r46 - r48;
    float r120 = r118 * r22;
    float r122 = r117 + r120;
    float r123 = r48 - r50;
    float r125 = uK[26];
    float r126 = r123 * r125;
    float r128 = r122 + r126;
    float r129 = r50 - r22;
    float r131 = r129 * r22;
    float r133 = r128 + r131;
    float r134 = uK[27];
    float r135 = clamp(r14, r134, max(r134, r82));
    float r136 = clamp(r13, r22, max(r22, r21));
    float r137 = sm(r22, r135, r19);
    float r138 = r136 - r135;
    float r139 = sm(r138, r136, r19);
    float r140 = r21 - r139;
    float r141 = r133 * r137;
    float r142 = r141 * r140;
    float r143 = uK[28];
    float r144 = uK[29];
    float r145 = r142 - r22;
    float r146 = r21 - r22;
    float r147 = dv(r145, r146);
    float r148 = r143 + (r144 - r143) * r147;
    float r149 = r0 * r5;
    float r150 = r1 * r5;
    float r151 = cos(r148);
    float r152 = sin(r148);
    float r153 = r149 * r151;
    float r154 = r150 * r152;
    float r155 = r153 - r154;
    float r156 = r149 * r152;
    float r157 = r150 * r151;
    float r158 = r156 + r157;
    float r159 = r155 - r22;
    float r160 = r158 - r22;
    vec3 t161 = fb(r159, r160);
    float r161 = t161.x; float r162 = t161.y; float r163 = t161.z;
    float r164 = uK[30];
    float r165 = uK[31];
    float r166 = r0 * r164;
    float r167 = r1 * r164;
    float r168 = cos(r165);
    float r169 = sin(r165);
    float r170 = r166 * r168;
    float r171 = r167 * r169;
    float r172 = r170 - r171;
    float r173 = r166 * r169;
    float r174 = r167 * r168;
    float r175 = r173 + r174;
    float r176 = r172 - r22;
    float r177 = r175 - r22;
    vec3 t178 = fb(r176, r177);
    float r178 = t178.x; float r179 = t178.y; float r180 = t178.z;
    float r181 = r161;
    float r182 = r179;
    float r183 = r180;
    float r184 = r181 * r114;
    float r185 = r182 * r114;
    float r186 = r183 * r114;
    float r187 = r184 + r22;
    float r188 = r185 + r22;
    float r189 = r186 + r22;
    float r190 = uK[32];
    float r191 = uK[33];
    float r192 = r6 * r12;
    float r193 = uK[34];
    float r194 = md(r192, r193);
    float r195 = floor(r194);
    float r196 = fr(r192);
    float r197 = dv(r195, r193);
    float r198 = step(r21, r195);
    float r199 = step(r24, r195);
    float r200 = step(r26, r195);
    float r201 = step(r11, r195);
    float r202 = step(r29, r195);
    float r203 = step(r31, r195);
    float r204 = step(r33, r195);
    float r205 = step(r35, r195);
    float r206 = step(r37, r195);
    float r207 = step(r39, r195);
    float r208 = step(r41, r195);
    float r209 = step(r43, r195);
    float r210 = step(r45, r195);
    float r211 = step(r47, r195);
    float r212 = step(r49, r195);
    float r213 = step(r16, r195);
    float r214 = uK[35];
    float r215 = step(r214, r195);
    float r216 = uK[36];
    float r217 = step(r216, r195);
    float r218 = uK[37];
    float r219 = step(r218, r195);
    float r220 = r21 - r198;
    float r223 = r220 * r21;
    float r224 = r198 - r199;
    float r227 = uK[38];
    float r228 = r224 * r227;
    float r230 = r223 + r228;
    float r231 = r199 - r200;
    float r234 = r231 * r71;
    float r236 = r230 + r234;
    float r237 = r200 - r201;
    float r239 = uK[39];
    float r240 = r237 * r239;
    float r242 = r236 + r240;
    float r243 = r201 - r202;
    float r246 = r243 * r21;
    float r248 = r242 + r246;
    float r249 = r202 - r203;
    float r251 = r249 * r114;
    float r253 = r248 + r251;
    float r254 = r203 - r204;
    float r256 = r254 * r22;
    float r258 = r253 + r256;
    float r259 = r204 - r205;
    float r262 = r259 * r71;
    float r264 = r258 + r262;
    float r265 = r205 - r206;
    float r268 = r265 * r227;
    float r270 = r264 + r268;
    float r271 = r206 - r207;
    float r273 = uK[40];
    float r274 = r271 * r273;
    float r276 = r270 + r274;
    float r277 = r207 - r208;
    float r280 = r277 * r21;
    float r282 = r276 + r280;
    float r283 = r208 - r209;
    float r285 = r283 * r114;
    float r287 = r282 + r285;
    float r288 = r209 - r210;
    float r290 = r288 * r71;
    float r292 = r287 + r290;
    float r293 = r210 - r211;
    float r295 = r293 * r239;
    float r297 = r292 + r295;
    float r298 = r211 - r212;
    float r300 = r298 * r93;
    float r302 = r297 + r300;
    float r303 = r212 - r213;
    float r305 = r303 * r22;
    float r307 = r302 + r305;
    float r308 = r213 - r215;
    float r310 = r308 * r114;
    float r312 = r307 + r310;
    float r313 = r215 - r217;
    float r315 = r313 * r21;
    float r317 = r312 + r315;
    float r318 = r217 - r219;
    float r321 = r318 * r273;
    float r323 = r317 + r321;
    float r324 = r219 - r22;
    float r326 = r324 * r227;
    float r328 = r323 + r326;
    float r329 = clamp(r191, r134, max(r134, r82));
    float r330 = clamp(r190, r22, max(r22, r21));
    float r331 = sm(r22, r329, r196);
    float r332 = r330 - r329;
    float r333 = sm(r332, r330, r196);
    float r334 = r21 - r333;
    float r335 = r328 * r331;
    float r336 = r335 * r334;
    float r337 = r197 * r227;
    float r338 = uK[41];
    float r339 = uK[42];
    float r340 = r142 - r22;
    float r341 = r21 - r22;
    float r342 = dv(r340, r341);
    float r343 = r338 + (r339 - r338) * r342;
    float r344 = uK[43];
    float r345 = r6 * r344;
    float r346 = r9 * r24;
    float r347 = uK[44];
    float r348 = r6 * r346;
    float r349 = md(r348, r16);
    float r350 = uK[45];
    float r351 = step(r350, r349);
    float r352 = step(r24, r349);
    float r353 = step(r26, r349);
    float r354 = step(r11, r349);
    float r355 = uK[46];
    float r356 = step(r355, r349);
    float r357 = step(r31, r349);
    float r358 = step(r33, r349);
    float r359 = step(r35, r349);
    float r360 = uK[47];
    float r361 = step(r360, r349);
    float r362 = step(r39, r349);
    float r363 = step(r43, r349);
    float r364 = r21 - r351;
    float r367 = r364 * r21;
    float r368 = r364 * r22;
    float r369 = r364 * r350;
    float r370 = r364 * r22;
    float r371 = r351 - r352;
    float r373 = r371 * r125;
    float r375 = r367 + r373;
    float r376 = r371 * r350;
    float r377 = r371 * r82;
    float r378 = uK[48];
    float r379 = r371 * r378;
    float r380 = r368 + r376;
    float r381 = r369 + r377;
    float r382 = r370 + r379;
    float r383 = r352 - r353;
    float r386 = r383 * r227;
    float r388 = r375 + r386;
    float r389 = r383 * r24;
    float r390 = r383 * r21;
    float r391 = uK[49];
    float r392 = r383 * r391;
    float r393 = r380 + r389;
    float r394 = r381 + r390;
    float r395 = r382 + r392;
    float r396 = r353 - r354;
    float r398 = uK[50];
    float r399 = r396 * r398;
    float r401 = r388 + r399;
    float r402 = r396 * r26;
    float r403 = r396 * r21;
    float r404 = uK[51];
    float r405 = r396 * r404;
    float r406 = r393 + r402;
    float r407 = r394 + r403;
    float r408 = r395 + r405;
    float r409 = r354 - r356;
    float r412 = r409 * r21;
    float r414 = r401 + r412;
    float r415 = r409 * r11;
    float r416 = r409 * r350;
    float r417 = uK[52];
    float r418 = r409 * r417;
    float r419 = r406 + r415;
    float r420 = r407 + r416;
    float r421 = r408 + r418;
    float r422 = r356 - r357;
    float r424 = r422 * r125;
    float r426 = r414 + r424;
    float r427 = r422 * r355;
    float r428 = r422 * r82;
    float r429 = uK[53];
    float r430 = r422 * r429;
    float r431 = r419 + r427;
    float r432 = r420 + r428;
    float r433 = r421 + r430;
    float r434 = r357 - r358;
    float r437 = r434 * r227;
    float r439 = r426 + r437;
    float r440 = r434 * r31;
    float r441 = r434 * r21;
    float r442 = r434 * r82;
    float r443 = r431 + r440;
    float r444 = r432 + r441;
    float r445 = r433 + r442;
    float r446 = r358 - r359;
    float r448 = r446 * r398;
    float r450 = r439 + r448;
    float r451 = r446 * r33;
    float r452 = r446 * r21;
    float r453 = uK[54];
    float r454 = r446 * r453;
    float r455 = r443 + r451;
    float r456 = r444 + r452;
    float r457 = r445 + r454;
    float r458 = r359 - r361;
    float r461 = r458 * r21;
    float r463 = r450 + r461;
    float r464 = r458 * r35;
    float r465 = r458 * r350;
    float r466 = uK[55];
    float r467 = r458 * r466;
    float r468 = r455 + r464;
    float r469 = r456 + r465;
    float r470 = r457 + r467;
    float r471 = r361 - r362;
    float r474 = r471 * r239;
    float r476 = r463 + r474;
    float r477 = r471 * r360;
    float r478 = r471 * r82;
    float r479 = uK[56];
    float r480 = r471 * r479;
    float r481 = r468 + r477;
    float r482 = r469 + r478;
    float r483 = r470 + r480;
    float r484 = r362 - r363;
    float r486 = r484 * r114;
    float r488 = r476 + r486;
    float r489 = r484 * r39;
    float r490 = r484 * r24;
    float r491 = uK[57];
    float r492 = r484 * r491;
    float r493 = r481 + r489;
    float r494 = r482 + r490;
    float r495 = r483 + r492;
    float r496 = r363 - r22;
    float r499 = r496 * r21;
    float r501 = r488 + r499;
    float r502 = r496 * r43;
    float r503 = r496 * r11;
    float r504 = uK[58];
    float r505 = r496 * r504;
    float r506 = r493 + r502;
    float r507 = r494 + r503;
    float r508 = r495 + r505;
    float r509 = r349 - r506;
    float r510 = dv(r509, r507);
    float r511 = clamp(r347, r134, max(r134, r82));
    float r512 = clamp(r125, r22, max(r22, r21));
    float r513 = sm(r22, r511, r510);
    float r514 = r512 - r511;
    float r515 = sm(r514, r512, r510);
    float r516 = r21 - r515;
    float r517 = r501 * r513;
    float r518 = r517 * r516;
    float r519 = uK[59];
    float r520 = uK[60];
    float r521 = r508 - r22;
    float r522 = r21 - r22;
    float r523 = dv(r521, r522);
    float r524 = r519 + (r520 - r519) * r523;
    float r525 = r345 + r524;
    float r526 = cos(r525);
    float r527 = sin(r525);
    float r528 = r0 * r526;
    float r529 = r1 * r527;
    float r530 = r528 - r529;
    float r531 = r0 * r527;
    float r532 = r1 * r526;
    float r533 = r531 + r532;
    float r534 = r530 * r343;
    float r535 = r533 * r343;
    float r536 = r534 - r22;
    float r537 = r535 - r22;
    float r538 = r508 - r22;
    float r539 = r21 - r22;
    float r540 = dv(r538, r539);
    float r541 = r26 + (r39 - r26) * r540;
    float r542 = sqrt(r536 * r536 + r537 * r537);
    float r543 = at2(r537, r536);
    float r544 = uK[61];
    float r545 = dv(r544, r541);
    float r546 = r545 * r82;
    float r547 = md(r543, r545);
    float r548 = r547 - r546;
    float r549 = abs(r548);
    float r550 = cos(r549);
    float r551 = r550 * r542;
    float r552 = sin(r549);
    float r553 = r552 * r542;
    float r554 = uK[62];
    float r555 = r6 * r554;
    float r556 = uK[63];
    float r557 = r551 * r556;
    float r558 = r553 * r556;
    float r559 = nz(r557, r558, r555);
    float r560 = r559 * r71;
    float r561 = r337 + r560;
    float r562 = r6 * r347;
    float r563 = r561 + r562;
    float r564 = fr(r563);
    float r565 = r518 - r22;
    float r566 = r21 - r22;
    float r567 = dv(r565, r566);
    float r568 = r125 + (r93 - r125) * r567;
    float r569 = uK[64];
    float r570 = uK[65];
    float r571 = r6 * r570 + r22;
    float r572 = r571 * r544;
    float r573 = sin(r572);
    float r574 = r573 * r82;
    float r575 = r574 + r82;
    float r576 = r575 - r22;
    float r577 = r21 - r22;
    float r578 = dv(r576, r577);
    float r579 = r569 + (r273 - r569) * r578;
    float r580 = r559 * r579;
    float r581 = r551 + r580;
    float r582 = r580 * r544;
    float r583 = sin(r582);
    float r584 = r553 + r583;
    float r585 = uK[66];
    float r586 = r336 - r22;
    float r587 = r21 - r22;
    float r588 = dv(r586, r587);
    float r589 = r585 + (r355 - r585) * r588;
    float r590 = r6 * r520;
    float r591 = sqrt(r581 * r581 + r584 * r584);
    float r592 = r591 * r589;
    float r593 = r592 + r590;
    float r594 = r593 * r544;
    float r595 = sin(r594);
    float r596 = sm(r569, r93, r595);
    float r597 = uK[67];
    float r598 = r142 - r22;
    float r599 = r21 - r22;
    float r600 = dv(r598, r599);
    float r601 = r479 + (r597 - r479) * r600;
    float r602 = r596 * r601;
    float r603 = clamp(r602, r22, max(r22, r21));
    vec3 t604 = hsv(r564, r568, r603);
    float r604 = t604.x; float r605 = t604.y; float r606 = t604.z;
    float r607 = max(r187, r604);
    float r608 = max(r188, r605);
    float r609 = max(r189, r606);

    fragColor = vec4(sat(r607), sat(r608), sat(r609), 1.0);
}
