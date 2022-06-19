using System;

namespace IDKEngine
{
    class TLAS
    {
        public readonly GLSLTlasNode[] Nodes;
        private int nodesUsed;
        public TLAS(int blasCount)
        {
            Nodes = new GLSLTlasNode[2 * blasCount];
        }

        public void Build(Span<AABB> worldSpaceBlasBounds)
        {

        }
    }
}
