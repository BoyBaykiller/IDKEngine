#version 460 core

#define TRANSPARENT_LAYERS AppInsert(TRANSPARENT_LAYERS)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict uniform image2D ImgResult;
layout(binding = 1) restrict readonly uniform image2DArray ImgRecordedColors;
layout(binding = 2) restrict readonly uniform image2DArray ImgRecordedDepths;
layout(binding = 3) restrict readonly uniform uimage2D ImgRecordedFragmentsCounter;


struct Fragment
{
    vec4 Color;
    float Depth;
};

// We need to declare this globally as passing it in to the function
// causes a huge performance degradation on AMD
Fragment fragments[TRANSPARENT_LAYERS];

void InsertionSort(Fragment newItem, int count);

void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(imgCoord, imgResultSize)))
    {
        return;
    }

    // TODO: Use fragment shader + stencil texture instead?
    uint fragmentCount = min(imageLoad(ImgRecordedFragmentsCounter, imgCoord).r, TRANSPARENT_LAYERS);
    if (fragmentCount == 0)
    {
        return;
    }

    for (int i = 0; i < fragmentCount; i++)
    {
        vec4 color = imageLoad(ImgRecordedColors, ivec3(imgCoord, i));
        float depth = imageLoad(ImgRecordedDepths, ivec3(imgCoord, i)).r;
        Fragment newFragment = Fragment(color, depth);

        InsertionSort(newFragment, i);
        // fragments[i] = newFragment;
    }

    // Front to back blending
    // glBlendEquation(mode: GL_FUNC_ADD)
    // glBlendFunc(sfactor: GL_ONE_MINUS_DST_ALPHA, dfactor: 1.0)
    vec4 accumulatedColor = vec4(0.0);
    for (int i = 0; i < fragmentCount; i++)
    {
        vec4 samplePremult = fragments[i].Color;
        
        float weightOfSample = 1.0 - accumulatedColor.a;
        accumulatedColor += weightOfSample * samplePremult;
    }

    // Finally calculate how much of the existing opaque color
    // reaches the camera after alpha blending is applied
    vec4 opaqueColor = imageLoad(ImgResult, imgCoord);
    accumulatedColor += (1.0 - accumulatedColor.a) * opaqueColor;

    imageStore(ImgResult, imgCoord, accumulatedColor);
}

void InsertionSort(Fragment newItem, int count)
{
    // Sort by depth in ascending oder
    for (int i = 0; i < count; i++)
    {
        if (newItem.Depth < fragments[i].Depth)
        {
            // Make space by shifting back all proceeding items by 1 
            for (int j = count; j > i; j--)
            {
                fragments[j] = fragments[j - 1];
            }

            fragments[i] = newItem;

            return;
        }
    }

    // All previous items are smaller so append new item
    fragments[count] = newItem;
}
