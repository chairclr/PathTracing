#include "Common.hlsl"

[[vk::binding(1, 0)]]
SamplerState SamplerView : SAMPLER : register(s0);
[[vk::binding(1, 0)]]
Texture2D MainTextureView : TEXTURE : register(t0);

PS_OUTPUT PSMain(PS_INPUT input)
{
    PS_OUTPUT output;
    
    float4 textureColor = MainTextureView.Sample(SamplerView, input.uv);
    
    float4 finalColor = textureColor;
    
    output.color = finalColor;

    return output;
}
