
//for render shader
float3 rotate(float3 p, float3 rotation)
{
    float3 a = normalize(rotation);
    float angle = length(rotation);
    if (abs(angle) < 0.001) return p;
    float s = sin(angle);
    float c = cos(angle);
    float r = 1.0 - c;
    float3x3 m = float3x3(
        a.x * a.x * r + c,
        a.y * a.x * r + a.z * s,
        a.z * a.x * r - a.y * s,
        a.x * a.y * r - a.z * s,
        a.y * a.y * r + c,
        a.z * a.y * r + a.x * s,
        a.x * a.z * r + a.y * s,
        a.y * a.z * r - a.x * s,
        a.z * a.z * r + c
    );
    return mul(m, p);
}

//for compute shader
inline float rand(float2 seed)
{
    return frac(sin(dot(seed.xy, float2(12.9898, 78.233))) * 43758.5453);
}

inline float3 rand3(float2 seed)
{
    return 2.0 * (float3(rand(seed * 1), rand(seed * 2), rand(seed * 3)) - 0.5);
}