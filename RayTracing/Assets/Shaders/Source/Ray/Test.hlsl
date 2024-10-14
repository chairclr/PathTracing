RWTexture2D<float4> TestImage : register(u0);

[numthreads(32, 32, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    float2 resolution = float2(1080.0, 1080.0);
    float2 uv = ((float2)id.xy / resolution) * 2.0 - 1.0;
    float3 cameraPos = float3(0.0, 0.0, 1.0);    

    float3 rayDir = normalize(float3(uv, -1.0)); 

    float3 sphereCenter = float3(0.0, 0.0, 0.0);
    float sphereRadius = 0.5;

    // Ray-sphere intersection
    float3 oc = cameraPos - sphereCenter;
    float a = dot(rayDir, rayDir);
    float b = 2.0 * dot(oc, rayDir);
    float c = dot(oc, oc) - sphereRadius * sphereRadius;
    
    float discriminant = b * b - 4.0 * a * c;

    float4 color;
    if (discriminant > 0.0)
    {
        // hit
        float t = (-b - sqrt(discriminant)) / (2.0 * a);
        float3 intersectionPoint = cameraPos + t * rayDir;
        float3 normal = normalize(intersectionPoint - sphereCenter);
        
        color = float4(normal * 0.5 + 0.5, 1.0);
    }
    else
    {
        // miss
        color = float4(uv.xy, 0.0, 1.0);
    }

    TestImage[id.xy] = color;
}
