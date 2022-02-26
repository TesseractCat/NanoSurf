// https://github.com/Unity-Technologies/Graphics/blob/baa890694c379cece690e26cefa7740f4cda540d/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl
half2 ParallaxOffset(half height, half amplitude, half3 viewDirTS)
{
    height = height * amplitude - amplitude / 2.0;
    half3 v = normalize(viewDirTS);
    v.z += 0.42;
    return height * (v.xy / v.z);
}

/*float2 WorldPosToScreenPos(float3 worldPos) {
    float4 CS = TransformWorldToHClip(worldPos);
    float4 SP = ComputeScreenPos(CS, _ProjectionParams.x);
    return SP.xy / SP.w;
}*/

#include "Noise.cginc"

float2 gnoise_dir(float2 p)
{
    p = p % 289;
    float x = (34 * p.x + 1) * p.x % 289 + p.y;
    x = (34 * x + 1) * x % 289;
    x = frac(x / 41) * 2 - 1;
    return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
}
float gnoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    float d00 = dot(gnoise_dir(ip), fp);
    float d01 = dot(gnoise_dir(ip + float2(0, 1)), fp - float2(0, 1));
    float d10 = dot(gnoise_dir(ip + float2(1, 0)), fp - float2(1, 0));
    float d11 = dot(gnoise_dir(ip + float2(1, 1)), fp - float2(1, 1));
    fp = fp * fp * fp * (fp * (fp * 6 - 15) + 10);
    return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x);
}
float gnoise2(float2 p)
{
    return float2(gnoise(p), gnoise(p + float2(1078.57, 323.133)));
}

float nrand(float3 uv) {
    return frac(sin(dot(uv, float3(12.9898, 78.233, 31.47474))) * 43758.5453);
}

float2 rotate(float2 p, float angle) {
    float cosAngle = cos(angle);
    float sinAngle = sin(angle);
    float2x2 rot = float2x2(cosAngle, -sinAngle, sinAngle, cosAngle);
    return mul(rot, p);
}

float dither(float In, float4 ScreenPosition)
{
    float2 uv = ScreenPosition.xy * _ScreenParams.xy;
    float DITHER_THRESHOLDS[16] =
    {
        1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
        13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
        4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
        16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
    };
    uint index = (uint(uv.x) % 4) * 4 + uint(uv.y) % 4;
    return In - DITHER_THRESHOLDS[index];
}

inline float2 unity_voronoi_noise_randomVector(float2 UV, float offset)
{
    float2x2 m = float2x2(15.27, 47.63, 99.41, 89.98);
    UV = frac(sin(mul(UV, m)) * 46839.32);
    return float2(sin(UV.y*+offset)*0.5+0.5, cos(UV.x*offset)*0.5+0.5);
}
void Unity_Voronoi_float(float2 UV, float AngleOffset, float CellDensity, out float Out, out float Cells)
{
    float2 g = floor(UV * CellDensity);
    float2 f = frac(UV * CellDensity);
    float t = 8.0;
    float3 res = float3(8.0, 0.0, 0.0);

    for(int y=-1; y<=1; y++)
    {
        for(int x=-1; x<=1; x++)
        {
            float2 lattice = float2(x,y);
            float2 offset = unity_voronoi_noise_randomVector(lattice + g, AngleOffset);
            float d = distance(lattice + offset, f);
            if(d < res.x)
            {
                res = float3(d, offset.x, offset.y);
                Out = res.x;
                Cells = res.y;
            }
        }
    }
}

// -------------------------------------------------- Color --------------------------------------------------

// https://www.chilliant.com/rgb2hsv.html
float3 RGBtoHCL(in float3 RGB)
{
    float HCLgamma = 3;
    float HCLy0 = 100;
    float HCLmaxL = 0.530454533953517; // == exp(HCLgamma / HCLy0) - 0.5

    float3 HCL;
    float H = 0;
    float U = min(RGB.r, min(RGB.g, RGB.b));
    float V = max(RGB.r, max(RGB.g, RGB.b));
    float Q = HCLgamma / HCLy0;
    HCL.y = V - U;
    if (HCL.y != 0)
    {
        H = atan2(RGB.g - RGB.b, RGB.r - RGB.g) / PI;
        Q *= U / V;
    }
    Q = exp(Q);
    HCL.x = frac(H / 2 - min(frac(H), frac(-H)) / 6);
    HCL.y *= Q;
    HCL.z = lerp(-U, V, Q) / (HCLmaxL * 2);
    return HCL;
}
float3 HCLtoRGB(in float3 HCL)
{
    float HCLgamma = 3;
    float HCLy0 = 100;
    float HCLmaxL = 0.530454533953517; // == exp(HCLgamma / HCLy0) - 0.5

    float3 RGB = 0;
    if (HCL.z != 0)
    {
        float H = HCL.x;
        float C = HCL.y;
        float L = HCL.z * HCLmaxL;
        float Q = exp((1 - C / (2 * L)) * (HCLgamma / HCLy0));
        float U = (2 * L - C) / (2 * Q - 1);
        float V = C / Q;
        float A = (H + min(frac(2 * H) / 4, frac(-2 * H) / 8)) * PI * 2;
        float T;
        H *= 6;
        if (H <= 0.999)
        {
            T = tan(A);
            RGB.r = 1;
            RGB.g = T / (1 + T);
        }
        else if (H <= 1.001)
        {
            RGB.r = 1;
            RGB.g = 1;
        }
        else if (H <= 2)
        {
            T = tan(A);
            RGB.r = (1 + T) / T;
            RGB.g = 1;
        }
        else if (H <= 3)
        {
            T = tan(A);
            RGB.g = 1;
            RGB.b = 1 + T;
        }
        else if (H <= 3.999)
        {
            T = tan(A);
            RGB.g = 1 / (1 + T);
            RGB.b = 1;
        }
        else if (H <= 4.001)
        {
            RGB.g = 0;
            RGB.b = 1;
        }
        else if (H <= 5)
        {
            T = tan(A);
            RGB.r = -1 / T;
            RGB.b = 1;
        }
        else
        {
            T = tan(A);
            RGB.r = 1;
            RGB.b = -T;
        }
        RGB = RGB * V + U;
    }
    return RGB;
}

float3 HUEtoRGB(in float H)
{
    float R = abs(H * 6 - 3) - 1;
    float G = 2 - abs(H * 6 - 2);
    float B = 2 - abs(H * 6 - 4);
    return saturate(float3(R,G,B));
}
float3 RGBtoHCV(in float3 RGB)
{
    float Epsilon = 1e-10;
    // Based on work by Sam Hocevar and Emil Persson
    float4 P = (RGB.g < RGB.b) ? float4(RGB.bg, -1.0, 2.0/3.0) : float4(RGB.gb, 0.0, -1.0/3.0);
    float4 Q = (RGB.r < P.x) ? float4(P.xyw, RGB.r) : float4(RGB.r, P.yzx);
    float C = Q.x - min(Q.w, Q.y);
    float H = abs((Q.w - Q.y) / (6 * C + Epsilon) + Q.z);
    return float3(H, C, Q.x);
}
float3 HSVtoRGB(in float3 HSV)
{
    float3 RGB = HUEtoRGB(HSV.x);
    return ((RGB - 1) * HSV.y + 1) * HSV.z;
}
float3 RGBtoHSV(in float3 RGB)
{
    float Epsilon = 1e-10;
    float3 HCV = RGBtoHCV(RGB);
    float S = HCV.y / (HCV.z + Epsilon);
    return float3(HCV.x, S, HCV.z);
}

float3 lerpHCL(float3 a, float3 b, float3 p) {
    return HCLtoRGB(lerp(RGBtoHCL(a), RGBtoHCL(b), p));
}
float3 lerpHSV(float3 a, float3 b, float3 p) {
    return HSVtoRGB(lerp(RGBtoHSV(a), RGBtoHSV(b), p));
}