#define GROUP_WISE_PROGRAM_STEPS 9 // Keep in sync between shader and client code!
#define DOWN_UP_SWEEP_PROGRAM_STEPS 7 // Keep in sync between shader and client code!

#define BLOCK_WISE_PROGRAM_LOCAL_SIZE_X (1 << GROUP_WISE_PROGRAM_STEPS)
#define DOWN_UP_SWEEP_PROGRAM_LOCAL_SIZE_X ((1 << DOWN_UP_SWEEP_PROGRAM_STEPS) / 2)
