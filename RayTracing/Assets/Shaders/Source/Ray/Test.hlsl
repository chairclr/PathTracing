RWTexture2D<float4> TestImage : register(u0);

[numthreads(32, 32, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    float2 uv = (float2)id.xy / float2(1920.0, 1080.0);
    TestImage[id.xy] = float4(uv.xy, 0.0, 1.0);
}
