using OpenTK.Mathematics;

namespace IDKEngine.Shapes
{
    public struct Frustum
    {
        // left, right, up, down, front, back
        public Vector4[] Planes;

        public Frustum(Matrix4 matrix)
        {
            Planes = new Vector4[6];

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    Planes[i * 2 + j].X = matrix[0, 3]+ (j == 0 ? matrix[0, i] : -matrix[0, i]);
                    Planes[i * 2 + j].Y = matrix[1, 3] + (j == 0 ? matrix[1, i] : -matrix[1, i]);
                    Planes[i * 2 + j].Z = matrix[2, 3] + (j == 0 ? matrix[2, i] : -matrix[2, i]);
                    Planes[i * 2 + j].W = matrix[3, 3] + (j == 0 ? matrix[3, i] : -matrix[3, i]);
                    Planes[i * 2 + j] *= (Planes[i * 2 + j].Xyz).Length;
                }
            }
        }
    }
}
