#version 460 core

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D Sampler0;
layout(binding = 1) uniform sampler2D Sampler1;
layout(binding = 2) uniform sampler2D Sampler2;

layout(std140, binding = 7) uniform SettingsUBO
{
    float Exposure;
    float Saturation;
    float Linear;
    float Peak;
    float Compression;
} settingsUBO;

vec3 LinearToSrgb(vec3 rgb);
vec3 AgX_DS(vec3 color_srgb, float exposure, float saturation, float linear, float peak, float compression);
vec3 Dither(vec3 color, ivec2 imgCoord);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 hdrColor = texture(Sampler0, uv).rgb;
    hdrColor += texture(Sampler1, uv).rgb;
    hdrColor += texture(Sampler2, uv).rgb;

    vec3 ldrColor = AgX_DS(hdrColor, settingsUBO.Exposure, settingsUBO.Saturation, settingsUBO.Linear, settingsUBO.Peak, settingsUBO.Compression);
    vec3 srgbColor = LinearToSrgb(ldrColor);

    srgbColor = Dither(srgbColor, imgCoord);

    imageStore(ImgResult, imgCoord, vec4(srgbColor, 1.0));
}

vec3 LinearToSrgb(vec3 linearRgb)
{
    // sRGB OETF, apply inverse of monitors transfer function

    bvec3 cutoff = lessThan(linearRgb, vec3(0.0031308));
    vec3 higher = vec3(1.055) * pow(linearRgb, vec3(1.0 / 2.4)) - vec3(0.055);
    vec3 lower = linearRgb * vec3(12.92);
    vec3 result = mix(higher, lower, cutoff);
    return result;
}

// Source: https://www.shadertoy.com/view/Dt3XDr

vec3 xyYToXYZ(vec3 xyY)
{
    float Y = xyY.z;
    float X = (xyY.x * Y) / xyY.y;
    float Z = ((1.0f - xyY.x - xyY.y) * Y) / xyY.y;

    return vec3(X, Y, Z);
}

vec3 Unproject(vec2 xy)
{
    return xyYToXYZ(vec3(xy.x, xy.y, 1));				
}

mat3 PrimariesToMatrix(vec2 xy_red, vec2 xy_green, vec2 xy_blue, vec2 xy_white)
{
    vec3 XYZ_red = Unproject(xy_red);
    vec3 XYZ_green = Unproject(xy_green);
    vec3 XYZ_blue = Unproject(xy_blue);
    vec3 XYZ_white = Unproject(xy_white);

    mat3 temp = mat3(XYZ_red.x,	  1.0, XYZ_red.z,
                    XYZ_green.x, 1.f, XYZ_green.z,
                    XYZ_blue.x,  1.0, XYZ_blue.z);
    vec3 scale = inverse(temp) * XYZ_white;

    return mat3(XYZ_red * scale.x, XYZ_green * scale.y, XYZ_blue * scale.z);
}

mat3 ComputeCompressionMatrix(vec2 xyR, vec2 xyG, vec2 xyB, vec2 xyW, float compression)
{
    float scale_factor = 1.0 / (1.0 - compression);
    vec2 R = mix(xyW, xyR, scale_factor);
    vec2 G = mix(xyW, xyG, scale_factor);
    vec2 B = mix(xyW, xyB, scale_factor);
    vec2 W = xyW;

    return PrimariesToMatrix(R, G, B, W);
}

float DualSection(float x, float linear, float peak)
{
    // Length of linear section
    float S = (peak * linear);
    if (x < S) {
        return x;
    } else {
        float C = peak / (peak - S);
        return peak - (peak - S) * exp((-C * (x - S)) / peak);
    }
}

vec3 DualSection(vec3 x, float linear, float peak)
{
    x.x = DualSection(x.x, linear, peak);
    x.y = DualSection(x.y, linear, peak);
    x.z = DualSection(x.z, linear, peak);
    return x;
}

vec3 AgX_DS(vec3 color_srgb, float exposure, float saturation, float linear, float peak, float compression)
{
    vec3 workingColor = max(color_srgb, 0.0f) * pow(2.0, exposure);

    mat3 sRGB_to_XYZ = PrimariesToMatrix(vec2(0.64, 0.33),
                                        vec2(0.3, 0.6), 
                                        vec2(0.15, 0.06), 
                                        vec2(0.3127, 0.3290));
    mat3 adjusted_to_XYZ = ComputeCompressionMatrix(vec2(0.64,0.33),
                                                    vec2(0.3,0.6), 
                                                    vec2(0.15,0.06), 
                                                    vec2(0.3127, 0.3290), compression);
    mat3 XYZ_to_adjusted = inverse(adjusted_to_XYZ);
    mat3 sRGB_to_adjusted = sRGB_to_XYZ * XYZ_to_adjusted;

    workingColor = sRGB_to_adjusted * workingColor;
    workingColor = clamp(DualSection(workingColor, linear, peak), 0.0, 1.0);

    vec3 luminanceWeight = vec3(0.2126729,  0.7151522,  0.0721750);
    vec3 desaturation = vec3(dot(workingColor, luminanceWeight));
    workingColor = mix(desaturation, workingColor, saturation);
    workingColor = clamp(workingColor, 0.0, 1.0);

    workingColor = inverse(sRGB_to_adjusted) * workingColor;

    return workingColor;
}

vec3 Dither(vec3 color, ivec2 imgCoord)
{
    // Source: https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/globals.hlsli#L824
    const float BayerMatrix8[8][8] =
    {
        { 1.0 / 65.0, 49.0 / 65.0, 13.0 / 65.0, 61.0 / 65.0, 4.0 / 65.0, 52.0 / 65.0, 16.0 / 65.0, 64.0 / 65.0 },
        { 33.0 / 65.0, 17.0 / 65.0, 45.0 / 65.0, 29.0 / 65.0, 36.0 / 65.0, 20.0 / 65.0, 48.0 / 65.0, 32.0 / 65.0 },
        { 9.0 / 65.0, 57.0 / 65.0, 5.0 / 65.0, 53.0 / 65.0, 12.0 / 65.0, 60.0 / 65.0, 8.0 / 65.0, 56.0 / 65.0 },
        { 41.0 / 65.0, 25.0 / 65.0, 37.0 / 65.0, 21.0 / 65.0, 44.0 / 65.0, 28.0 / 65.0, 40.0 / 65.0, 24.0 / 65.0 },
        { 3.0 / 65.0, 51.0 / 65.0, 15.0 / 65.0, 63.0 / 65.0, 2.0 / 65.0, 50.0 / 65.0, 14.0 / 65.0, 62.0 / 65.0 },
        { 35.0 / 65.0, 19.0 / 65.0, 47.0 / 65.0, 31.0 / 65.0, 34.0 / 65.0, 18.0 / 65.0, 46.0 / 65.0, 30.0 / 65.0 },
        { 11.0 / 65.0, 59.0 / 65.0, 7.0 / 65.0, 55.0 / 65.0, 10.0 / 65.0, 58.0 / 65.0, 6.0 / 65.0, 54.0 / 65.0 },
        { 43.0 / 65.0, 27.0 / 65.0, 39.0 / 65.0, 23.0 / 65.0, 42.0 / 65.0, 26.0 / 65.0, 38.0 / 65.0, 22.0 / 65.0 }
    };
    uint len = BayerMatrix8.length() * BayerMatrix8[0].length();

    int x = imgCoord.x % BayerMatrix8[0].length();
    int y = imgCoord.y % BayerMatrix8.length();
    float ditherVal = (BayerMatrix8[x][y] - 0.5) / len;

    vec3 dithered = color + ditherVal;

    return dithered;
}