struct Ray
{
    vec3 Origin;
    vec3 Direction;
};

Ray RayTransform(Ray ray, mat4 model)
{
    vec3 newOrigin = (model * vec4(ray.Origin, 1.0)).xyz;
    vec3 newDirection = (model * vec4(ray.Direction, 0.0)).xyz;
    return Ray(newOrigin, newDirection);
}
