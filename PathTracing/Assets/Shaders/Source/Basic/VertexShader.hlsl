#include "Common.hlsl"

/*[[vk::binding(0, 0)]]
cbuffer VertexShaderBuffer : register(b0)
{
    row_major float4x4 ViewProjection; // 64 bytes
};*/

PS_INPUT VSMain(VS_INPUT input)
{
    PS_INPUT output;
    
    float4 worldPosition = float4(input.position, 1.0);
    
    output.position = worldPosition;//mul(worldPosition, ViewProjection);
    
    return output;
}
