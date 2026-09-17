#version 150

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
    float r6 = r0 * r5;
    float r7 = r1 * r5;
    float r8 = uTime;
    float r9 = uK[1];
    float r10 = uK[2];
    float r11 = r9 * r10;
    float r12 = uK[3];
    float r13 = r11 * r12;
    float r14 = uK[4];
    float r15 = uK[5];
    float r16 = r8 * r13;
    float r17 = uK[6];
    float r18 = md(r16, r17);
    float r19 = floor(r18);
    float r20 = fr(r16);
    float r22 = uK[7];
    float r23 = uK[8];
    float r24 = step(r22, r19);
    float r25 = uK[9];
    float r26 = step(r25, r19);
    float r27 = uK[10];
    float r28 = step(r27, r19);
    float r29 = step(r12, r19);
    float r30 = uK[11];
    float r31 = step(r30, r19);
    float r32 = uK[12];
    float r33 = step(r32, r19);
    float r34 = uK[13];
    float r35 = step(r34, r19);
    float r36 = uK[14];
    float r37 = step(r36, r19);
    float r38 = uK[15];
    float r39 = step(r38, r19);
    float r40 = uK[16];
    float r41 = step(r40, r19);
    float r42 = uK[17];
    float r43 = step(r42, r19);
    float r44 = uK[18];
    float r45 = step(r44, r19);
    float r46 = uK[19];
    float r47 = step(r46, r19);
    float r48 = uK[20];
    float r49 = step(r48, r19);
    float r50 = uK[21];
    float r51 = step(r50, r19);
    float r52 = r22 - r24;
    float r54 = r52 * r22;
    float r55 = r24 - r26;
    float r57 = r55 * r23;
    float r59 = r54 + r57;
    float r60 = r26 - r28;
    float r62 = r60 * r23;
    float r64 = r59 + r62;
    float r65 = r28 - r29;
    float r67 = r65 * r23;
    float r69 = r64 + r67;
    float r70 = r29 - r31;
    float r72 = uK[22];
    float r73 = r70 * r72;
    float r75 = r69 + r73;
    float r76 = r31 - r33;
    float r78 = r76 * r23;
    float r80 = r75 + r78;
    float r81 = r33 - r35;
    float r83 = uK[23];
    float r84 = r81 * r83;
    float r86 = r80 + r84;
    float r87 = r35 - r37;
    float r89 = r87 * r23;
    float r91 = r86 + r89;
    float r92 = r37 - r39;
    float r94 = uK[24];
    float r95 = r92 * r94;
    float r97 = r91 + r95;
    float r98 = r39 - r41;
    float r100 = r98 * r23;
    float r102 = r97 + r100;
    float r103 = r41 - r43;
    float r105 = r103 * r23;
    float r107 = r102 + r105;
    float r108 = r43 - r45;
    float r110 = r108 * r23;
    float r112 = r107 + r110;
    float r113 = r45 - r47;
    float r115 = uK[25];
    float r116 = r113 * r115;
    float r118 = r112 + r116;
    float r119 = r47 - r49;
    float r121 = r119 * r23;
    float r123 = r118 + r121;
    float r124 = r49 - r51;
    float r126 = uK[26];
    float r127 = r124 * r126;
    float r129 = r123 + r127;
    float r130 = r51 - r23;
    float r132 = r130 * r23;
    float r134 = r129 + r132;
    float r135 = uK[27];
    float r136 = clamp(r15, r135, max(r135, r83));
    float r137 = clamp(r14, r23, max(r23, r22));
    float r138 = sm(r23, r136, r20);
    float r139 = r137 - r136;
    float r140 = sm(r139, r137, r20);
    float r141 = r22 - r140;
    float r142 = r134 * r138;
    float r143 = r142 * r141;
    float r144 = uK[28];
    float r145 = uK[29];
    float r146 = r143 - r23;
    float r147 = r22 - r23;
    float r148 = dv(r146, r147);
    float r149 = r144 + (r145 - r144) * r148;
    float r150 = cos(r149);
    float r151 = sin(r149);
    float r152 = r6 * r150;
    float r153 = r7 * r151;
    float r154 = r152 - r153;
    float r155 = r6 * r151;
    float r156 = r7 * r150;
    float r157 = r155 + r156;
    vec3 t158 = fb(r154, r157);
    float r158 = t158.x; float r159 = t158.y; float r160 = t158.z;
    float r161 = uK[30];
    float r162 = r0 * r161;
    float r163 = r1 * r161;
    float r164 = uK[31];
    float r165 = cos(r164);
    float r166 = sin(r164);
    float r167 = r162 * r165;
    float r168 = r163 * r166;
    float r169 = r167 - r168;
    float r170 = r162 * r166;
    float r171 = r163 * r165;
    float r172 = r170 + r171;
    vec3 t173 = fb(r169, r172);
    float r173 = t173.x; float r174 = t173.y; float r175 = t173.z;
    float r176 = r158;
    float r177 = r174;
    float r178 = r175;
    float r179 = r176 * r115;
    float r180 = r177 * r115;
    float r181 = r178 * r115;
    float r182 = r179 + r23;
    float r183 = r180 + r23;
    float r184 = r181 + r23;
    float r185 = uK[32];
    float r186 = uK[33];
    float r187 = r8 * r13;
    float r188 = uK[34];
    float r189 = md(r187, r188);
    float r190 = floor(r189);
    float r191 = fr(r187);
    float r192 = dv(r190, r188);
    float r193 = step(r22, r190);
    float r194 = step(r25, r190);
    float r195 = step(r27, r190);
    float r196 = step(r12, r190);
    float r197 = step(r30, r190);
    float r198 = step(r32, r190);
    float r199 = step(r34, r190);
    float r200 = step(r36, r190);
    float r201 = step(r38, r190);
    float r202 = step(r40, r190);
    float r203 = step(r42, r190);
    float r204 = step(r44, r190);
    float r205 = step(r46, r190);
    float r206 = step(r48, r190);
    float r207 = step(r50, r190);
    float r208 = step(r17, r190);
    float r209 = uK[35];
    float r210 = step(r209, r190);
    float r211 = uK[36];
    float r212 = step(r211, r190);
    float r213 = uK[37];
    float r214 = step(r213, r190);
    float r215 = r22 - r193;
    float r218 = r215 * r22;
    float r219 = r193 - r194;
    float r222 = uK[38];
    float r223 = r219 * r222;
    float r225 = r218 + r223;
    float r226 = r194 - r195;
    float r229 = r226 * r72;
    float r231 = r225 + r229;
    float r232 = r195 - r196;
    float r234 = uK[39];
    float r235 = r232 * r234;
    float r237 = r231 + r235;
    float r238 = r196 - r197;
    float r241 = r238 * r22;
    float r243 = r237 + r241;
    float r244 = r197 - r198;
    float r246 = r244 * r115;
    float r248 = r243 + r246;
    float r249 = r198 - r199;
    float r251 = r249 * r23;
    float r253 = r248 + r251;
    float r254 = r199 - r200;
    float r257 = r254 * r72;
    float r259 = r253 + r257;
    float r260 = r200 - r201;
    float r263 = r260 * r222;
    float r265 = r259 + r263;
    float r266 = r201 - r202;
    float r268 = uK[40];
    float r269 = r266 * r268;
    float r271 = r265 + r269;
    float r272 = r202 - r203;
    float r275 = r272 * r22;
    float r277 = r271 + r275;
    float r278 = r203 - r204;
    float r280 = r278 * r115;
    float r282 = r277 + r280;
    float r283 = r204 - r205;
    float r285 = r283 * r72;
    float r287 = r282 + r285;
    float r288 = r205 - r206;
    float r290 = r288 * r234;
    float r292 = r287 + r290;
    float r293 = r206 - r207;
    float r295 = r293 * r94;
    float r297 = r292 + r295;
    float r298 = r207 - r208;
    float r300 = r298 * r23;
    float r302 = r297 + r300;
    float r303 = r208 - r210;
    float r305 = r303 * r115;
    float r307 = r302 + r305;
    float r308 = r210 - r212;
    float r310 = r308 * r22;
    float r312 = r307 + r310;
    float r313 = r212 - r214;
    float r316 = r313 * r268;
    float r318 = r312 + r316;
    float r319 = r214 - r23;
    float r321 = r319 * r222;
    float r323 = r318 + r321;
    float r324 = clamp(r186, r135, max(r135, r83));
    float r325 = clamp(r185, r23, max(r23, r22));
    float r326 = sm(r23, r324, r191);
    float r327 = r325 - r324;
    float r328 = sm(r327, r325, r191);
    float r329 = r22 - r328;
    float r330 = r323 * r326;
    float r331 = r330 * r329;
    float r332 = r192 * r222;
    float r333 = uK[41];
    float r334 = r8 * r333;
    float r335 = r11 * r25;
    float r336 = uK[42];
    float r337 = r8 * r335;
    float r338 = md(r337, r17);
    float r339 = uK[43];
    float r340 = step(r339, r338);
    float r341 = step(r25, r338);
    float r342 = step(r27, r338);
    float r343 = step(r12, r338);
    float r344 = uK[44];
    float r345 = step(r344, r338);
    float r346 = step(r32, r338);
    float r347 = step(r34, r338);
    float r348 = step(r36, r338);
    float r349 = uK[45];
    float r350 = step(r349, r338);
    float r351 = step(r40, r338);
    float r352 = step(r44, r338);
    float r353 = r22 - r340;
    float r356 = r353 * r22;
    float r357 = r353 * r23;
    float r358 = r353 * r339;
    float r359 = r353 * r23;
    float r360 = r340 - r341;
    float r362 = r360 * r126;
    float r364 = r356 + r362;
    float r365 = r360 * r339;
    float r366 = r360 * r83;
    float r367 = uK[46];
    float r368 = r360 * r367;
    float r369 = r357 + r365;
    float r370 = r358 + r366;
    float r371 = r359 + r368;
    float r372 = r341 - r342;
    float r375 = r372 * r222;
    float r377 = r364 + r375;
    float r378 = r372 * r25;
    float r379 = r372 * r22;
    float r380 = uK[47];
    float r381 = r372 * r380;
    float r382 = r369 + r378;
    float r383 = r370 + r379;
    float r384 = r371 + r381;
    float r385 = r342 - r343;
    float r387 = uK[48];
    float r388 = r385 * r387;
    float r390 = r377 + r388;
    float r391 = r385 * r27;
    float r392 = r385 * r22;
    float r393 = uK[49];
    float r394 = r385 * r393;
    float r395 = r382 + r391;
    float r396 = r383 + r392;
    float r397 = r384 + r394;
    float r398 = r343 - r345;
    float r401 = r398 * r22;
    float r403 = r390 + r401;
    float r404 = r398 * r12;
    float r405 = r398 * r339;
    float r406 = uK[50];
    float r407 = r398 * r406;
    float r408 = r395 + r404;
    float r409 = r396 + r405;
    float r410 = r397 + r407;
    float r411 = r345 - r346;
    float r413 = r411 * r126;
    float r415 = r403 + r413;
    float r416 = r411 * r344;
    float r417 = r411 * r83;
    float r418 = uK[51];
    float r419 = r411 * r418;
    float r420 = r408 + r416;
    float r421 = r409 + r417;
    float r422 = r410 + r419;
    float r423 = r346 - r347;
    float r426 = r423 * r222;
    float r428 = r415 + r426;
    float r429 = r423 * r32;
    float r430 = r423 * r22;
    float r431 = r423 * r83;
    float r432 = r420 + r429;
    float r433 = r421 + r430;
    float r434 = r422 + r431;
    float r435 = r347 - r348;
    float r437 = r435 * r387;
    float r439 = r428 + r437;
    float r440 = r435 * r34;
    float r441 = r435 * r22;
    float r442 = uK[52];
    float r443 = r435 * r442;
    float r444 = r432 + r440;
    float r445 = r433 + r441;
    float r446 = r434 + r443;
    float r447 = r348 - r350;
    float r450 = r447 * r22;
    float r452 = r439 + r450;
    float r453 = r447 * r36;
    float r454 = r447 * r339;
    float r455 = uK[53];
    float r456 = r447 * r455;
    float r457 = r444 + r453;
    float r458 = r445 + r454;
    float r459 = r446 + r456;
    float r460 = r350 - r351;
    float r463 = r460 * r234;
    float r465 = r452 + r463;
    float r466 = r460 * r349;
    float r467 = r460 * r83;
    float r468 = uK[54];
    float r469 = r460 * r468;
    float r470 = r457 + r466;
    float r471 = r458 + r467;
    float r472 = r459 + r469;
    float r473 = r351 - r352;
    float r475 = r473 * r115;
    float r477 = r465 + r475;
    float r478 = r473 * r40;
    float r479 = r473 * r25;
    float r480 = uK[55];
    float r481 = r473 * r480;
    float r482 = r470 + r478;
    float r483 = r471 + r479;
    float r484 = r472 + r481;
    float r485 = r352 - r23;
    float r488 = r485 * r22;
    float r490 = r477 + r488;
    float r491 = r485 * r44;
    float r492 = r485 * r12;
    float r493 = uK[56];
    float r494 = r485 * r493;
    float r495 = r482 + r491;
    float r496 = r483 + r492;
    float r497 = r484 + r494;
    float r498 = r338 - r495;
    float r499 = dv(r498, r496);
    float r500 = clamp(r336, r135, max(r135, r83));
    float r501 = clamp(r126, r23, max(r23, r22));
    float r502 = sm(r23, r500, r499);
    float r503 = r501 - r500;
    float r504 = sm(r503, r501, r499);
    float r505 = r22 - r504;
    float r506 = r490 * r502;
    float r507 = r506 * r505;
    float r508 = uK[57];
    float r509 = uK[58];
    float r510 = r497 - r23;
    float r511 = r22 - r23;
    float r512 = dv(r510, r511);
    float r513 = r508 + (r509 - r508) * r512;
    float r514 = r334 + r513;
    float r515 = cos(r514);
    float r516 = sin(r514);
    float r517 = r0 * r515;
    float r518 = r1 * r516;
    float r519 = r517 - r518;
    float r520 = r0 * r516;
    float r521 = r1 * r515;
    float r522 = r520 + r521;
    float r523 = uK[59];
    float r524 = uK[60];
    float r525 = r143 - r23;
    float r526 = r22 - r23;
    float r527 = dv(r525, r526);
    float r528 = r523 + (r524 - r523) * r527;
    float r529 = r519 * r528;
    float r530 = r522 * r528;
    float r531 = r497 - r23;
    float r532 = r22 - r23;
    float r533 = dv(r531, r532);
    float r534 = r27 + (r40 - r27) * r533;
    float r535 = sqrt(r529 * r529 + r530 * r530);
    float r536 = at2(r530, r529);
    float r537 = uK[61];
    float r538 = dv(r537, r534);
    float r539 = r538 * r83;
    float r540 = md(r536, r538);
    float r541 = r540 - r539;
    float r542 = abs(r541);
    float r543 = cos(r542);
    float r544 = r543 * r535;
    float r545 = sin(r542);
    float r546 = r545 * r535;
    float r547 = uK[62];
    float r548 = r8 * r547;
    float r549 = uK[63];
    float r550 = r544 * r549;
    float r551 = r546 * r549;
    float r552 = nz(r550, r551, r548);
    float r553 = r552 * r72;
    float r554 = r332 + r553;
    float r555 = r8 * r336;
    float r556 = r554 + r555;
    float r557 = fr(r556);
    float r558 = r507 - r23;
    float r559 = r22 - r23;
    float r560 = dv(r558, r559);
    float r561 = r126 + (r94 - r126) * r560;
    float r562 = uK[64];
    float r563 = uK[65];
    float r564 = r8 * r563 + r23;
    float r565 = r564 * r537;
    float r566 = sin(r565);
    float r567 = r566 * r83;
    float r568 = r567 + r83;
    float r569 = r568 - r23;
    float r570 = r22 - r23;
    float r571 = dv(r569, r570);
    float r572 = r562 + (r268 - r562) * r571;
    float r573 = r552 * r572;
    float r574 = r544 + r573;
    float r575 = r573 * r537;
    float r576 = sin(r575);
    float r577 = r546 + r576;
    float r578 = uK[66];
    float r579 = r331 - r23;
    float r580 = r22 - r23;
    float r581 = dv(r579, r580);
    float r582 = r578 + (r344 - r578) * r581;
    float r583 = r8 * r509;
    float r584 = sqrt(r574 * r574 + r577 * r577);
    float r585 = r584 * r582;
    float r586 = r585 + r583;
    float r587 = r586 * r537;
    float r588 = sin(r587);
    float r589 = sm(r562, r94, r588);
    float r590 = uK[67];
    float r591 = r143 - r23;
    float r592 = r22 - r23;
    float r593 = dv(r591, r592);
    float r594 = r468 + (r590 - r468) * r593;
    float r595 = r589 * r594;
    float r596 = clamp(r595, r23, max(r23, r22));
    vec3 t597 = hsv(r557, r561, r596);
    float r597 = t597.x; float r598 = t597.y; float r599 = t597.z;
    float r600 = max(r182, r597);
    float r601 = max(r183, r598);
    float r602 = max(r184, r599);

    fragColor = vec4(sat(r600), sat(r601), sat(r602), 1.0);
}
