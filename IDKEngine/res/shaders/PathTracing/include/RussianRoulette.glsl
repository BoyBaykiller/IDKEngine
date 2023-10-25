#ifndef RussianRoulette_H
#define RussianRoulette_H

AppInclude(include/Random.glsl)

bool RussianRouletteTerminateRay(inout vec3 throughput)
{
    float p = max(throughput.x, max(throughput.y, throughput.z));
    if (GetRandomFloat01() > p)
    {
        return true;
    }
    throughput /= p;
    return false;
}

#endif