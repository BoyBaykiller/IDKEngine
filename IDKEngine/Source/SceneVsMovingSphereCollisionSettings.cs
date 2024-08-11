using IDKEngine.Shapes;

namespace IDKEngine
{
    public record struct SceneVsMovingSphereCollisionSettings
    {
        public bool IsEnabled;
        public Intersections.SceneVsMovingSphereSettings Collision;
    }
}
