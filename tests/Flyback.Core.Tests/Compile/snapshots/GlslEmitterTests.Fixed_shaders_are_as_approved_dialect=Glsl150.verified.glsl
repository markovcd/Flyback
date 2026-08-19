#version 150

out vec2 vUv;

void main()
{
    vec2 p = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}

// ----------------------------------------------------------------
#version 150

uniform float uScaleX;
uniform float uScaleY;

out vec2 vUv;

void main()
{
    vec2 p = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
    vUv = p;
    gl_Position = vec4((p * 2.0 - 1.0) * vec2(uScaleX, uScaleY), 0.0, 1.0);
}

// ----------------------------------------------------------------
#version 150

uniform sampler2D uTexture;

in vec2 vUv;
out vec4 fragColor;

void main()
{
    fragColor = vec4(texture(uTexture, vUv).rgb, 1.0);
}
