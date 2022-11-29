#version 460 core

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);

}