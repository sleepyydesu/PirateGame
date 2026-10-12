#ifndef TFW_URP_COMMON_INCLUDED
#define TFW_URP_COMMON_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// ShadowCaster-pass globals. This URP version's headers don't declare them,
// but the renderer still sets them every shadow pass.
// (If a future Unity/URP update ever reports "redefinition of _LightDirection",
//  simply delete these two lines.)
float3 _LightDirection;
float3 _LightPosition;

// Built-in called it UnpackScaleNormal; URP calls it UnpackNormalScale.
#define UnpackScaleNormal UnpackNormalScale

float3 mod2D289(float3 x){ return x - floor(x*(1.0/289.0))*289.0; }
float2 mod2D289(float2 x){ return x - floor(x*(1.0/289.0))*289.0; }
float3 permute(float3 x){ return mod2D289(((x*34.0)+1.0)*x); }
float snoise(float2 v)
{
    const float4 C = float4(0.211324865405187,0.366025403784439,-0.577350269189626,0.024390243902439);
    float2 i = floor(v + dot(v, C.yy));
    float2 x0 = v - i + dot(i, C.xx);
    float2 i1 = (x0.x > x0.y) ? float2(1,0) : float2(0,1);
    float4 x12 = x0.xyxy + C.xxzz; x12.xy -= i1;
    i = mod2D289(i);
    float3 p = permute(permute(i.y + float3(0, i1.y, 1)) + i.x + float3(0, i1.x, 1));
    float3 m = max(0.5 - float3(dot(x0,x0), dot(x12.xy,x12.xy), dot(x12.zw,x12.zw)), 0.0);
    m = m*m; m = m*m;
    float3 x = 2.0*frac(p*C.www) - 1.0;
    float3 h = abs(x) - 0.5;
    float3 ox = floor(x + 0.5);
    float3 a0 = x - ox;
    m *= 1.79284291400159 - 0.85373472095314*(a0*a0+h*h);
    float3 g;
    g.x = a0.x*x0.x + h.x*x0.y;
    g.yz = a0.yz*x12.xz + h.yz*x12.yw;
    return 130.0*dot(m,g);
}
float3 RotateAroundAxis(float3 center, float3 original, float3 u, float angle)
{
    original -= center;
    float C = cos(angle); float S = sin(angle); float t = 1 - C;
    float3x3 m = float3x3(
        t*u.x*u.x + C,     t*u.x*u.y - S*u.z, t*u.x*u.z + S*u.y,
        t*u.x*u.y + S*u.z, t*u.y*u.y + C,     t*u.y*u.z - S*u.x,
        t*u.x*u.z - S*u.y, t*u.y*u.z + S*u.x, t*u.z*u.z + C);
    return mul(m, original) + center;
}
float3 TFW_WindVec(float3 dir)
{
    float fx = (dir.x != 0.0) ? 1.0 : 0.0;
    float fz = (dir.z != 0.0) ? 1.0 : 0.0;
    return lerp(float3(0,0,1), dir, fx + fz);
}
#endif