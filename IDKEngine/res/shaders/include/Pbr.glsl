#ifndef Pbr_H
#define Pbr_H

float GetAttenuationFactor(float squareDist, float lightRadius)
{
    float lightInvRadius = 1.0 / max(lightRadius, 0.0001);
    float distanceSquared = max(squareDist, 0.0001);

    float factor = 1.0 / (distanceSquared * lightInvRadius * lightInvRadius);

    return factor;
}

#endif