using OpenTK.Mathematics;
using IDKEngine.Utils;

namespace IDKEngine
{
    public record struct Transformation
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

        public Transformation WithScale(Vector3 s)
        {
            Scale = s;
            return this;
        }

        public Transformation WithTranslation(float x, float y, float z)
        {
            return WithTranslation(new Vector3(x, y, z));
        }

        public Transformation WithTranslation(Vector3 position)
        {
            Translation = position;
            return this;
        }

        public Transformation WithRotationDeg(float x, float y, float z)
        {
            return WithRotationRad(MyMath.DegreesToRadians(x), MyMath.DegreesToRadians(y), MyMath.DegreesToRadians(z));
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

        public static Transformation operator +(in Transformation lhs, in Transformation rhs)
        {
            Transformation result;
            result.Translation = lhs.Translation + rhs.Translation;
            result.Scale = lhs.Scale + rhs.Scale;
            result.Rotation = lhs.Rotation + rhs.Rotation;
            return result;
        }

        public static Transformation operator -(in Transformation lhs, in Transformation rhs)
        {
            Transformation result;
            result.Translation = lhs.Translation - rhs.Translation;
            result.Scale = lhs.Scale - rhs.Scale;
            result.Rotation = lhs.Rotation - rhs.Rotation;
            return result;
        }
    }
}
