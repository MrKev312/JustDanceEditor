#version 450

layout(push_constant) uniform QuadPushConstants
{
    vec4 vertices[4];
} pushData;

layout(location = 0) out vec2 outUv;

const int indices[6] = int[6](0, 1, 2, 0, 2, 3);

void main()
{
    vec4 vertex = pushData.vertices[indices[gl_VertexIndex]];
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
    outUv = vertex.zw;
}
