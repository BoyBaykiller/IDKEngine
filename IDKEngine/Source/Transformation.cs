using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct Transformation
    {
        public Vector3 Translation;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Scale = new Vector3(1.0f);

        public Matrix4 Matrix => Matrix4.CreateScale(Scale) * Matrix4.CreateFromQuaternion(Rotation) * Matrix4.CreateTranslation(Translation);

        public Transformation()
        {
        }

        public Transformation WithScale(float s)
        {
            return WithScale(new Vector3(s));
        }

        public Transformation WithScale(in Vector3 s)
        {
            Scale = s;
            return this;
        }

        public Transformation WithTranslation(float x, float y, float z)
        {
            return WithTranslation(new Vector3(x, y, z));
        }

        public Transformation WithTranslation(in Vector3 position)
        {
            Translation = position;
            return this;
        }

        public Transformation WithRotationDeg(float x, float y, float z)
        {
            return WithRotationRad(MathHelper.DegreesToRadians(x), MathHelper.DegreesToRadians(y), MathHelper.DegreesToRadians(z));
        }

        public Transformation WithRotationRad(float x, float y, float z)
        {
            Rotation = Quaternion.FromEulerAngles(x, y, z);
            return this;
        }

        public static Transformation FromMatrix(in Matrix4 mat)
        {
            Transformation transformation;
            transformation.Scale = mat.ExtractScale();
            transformation.Rotation = mat.ExtractRotation();
            transformation.Translation = mat.ExtractTranslation();

            return transformation;
        }
    }
}
