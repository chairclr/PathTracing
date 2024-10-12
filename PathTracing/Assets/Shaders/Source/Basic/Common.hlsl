struct VS_INPUT
{
    float3 position : POSITION;
};

struct PS_INPUT
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

struct PS_OUTPUT
{
    float4 color : SV_TARGET;
};
