// atomicMax causes the shader to run in wave64 mode under gfx1010 (RDNA).
// Having the work group size be 32 makes it run in wave32 mode.
// Unfortunately for gfx1100 (RDNA3) and possibly other architectures it still runs in wave64.

#define N_HIT_PROGRAM_LOCAL_SIZE_X 32