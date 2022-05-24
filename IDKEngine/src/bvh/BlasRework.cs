using IDKEngine.Render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IDKEngine
{
    class BlasRework
    {
        public readonly ModelSystem ModelSystem;
        public BlasRework(ModelSystem modelSystem)
        {
            ModelSystem = modelSystem;
        }

        //private unsafe GLSLBlasNode[] BuildBlasNode(GLSLMesh mesh, GLSLDrawCommand cmd)
        //{
        //    GLSLBlasNode[] node = new GLSLBlasNode[cmd.Count * 2 - 1];
        //    fixed (void* ptr = &ModelSystem.Vertices[cmd.FirstIndex])
        //    {
        //        Span<GLSLTriangle> triangles = new Span<GLSLTriangle>(ptr, cmd.Count / 3);


        //    }
        //}
    }
}
