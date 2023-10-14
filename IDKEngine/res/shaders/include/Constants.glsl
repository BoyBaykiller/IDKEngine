#ifndef Constants_H
#define Constants_H

#define MATERIAL_EMISSIVE_FACTOR 10.0

// These constants are used in shader and client code. Keep in sync!
#define GPU_MAX_UBO_POINT_SHADOW_COUNT 16
#define GPU_MAX_UBO_LIGHT_COUNT 256

#define TEMPORAL_ANTI_ALIASING_MODE_NO_AA 0
#define TEMPORAL_ANTI_ALIASING_MODE_TAA 1
#define TEMPORAL_ANTI_ALIASING_MODE_FSR2 2

#endif