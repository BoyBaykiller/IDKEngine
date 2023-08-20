#ifndef ShadingRateClassification_Constants_H
#define ShadingRateClassification_Constants_H

#define SHADING_RATE_1_INVOCATION_PER_PIXEL_NV 0u
#define SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV 1u
#define SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV 2u
#define SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV 3u
#define SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV 4u
#define TILE_SIZE 16

// used in shader and client code - keep in sync!
#define DEBUG_MODE_SHADING_RATES 1
#define DEBUG_MODE_SPEED 2
#define DEBUG_MODE_LUMINANCE 3
#define DEBUG_MODE_LUMINANCE_VARIANCE 4

#endif