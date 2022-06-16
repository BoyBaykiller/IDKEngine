using System;

namespace IDKEngine
{
    class TLAS
    {
        private int nodesUsed;
        public readonly GLSLTlasNode[] Nodes;
        public TLAS(Span<BLAS> blasSpan)
        {
            //Nodes = new GLSLTLASNode[2 * blasSpan.Length];

            //nodesUsed = 1;
            //for (int i = 0; i < blasSpan.Length; i++)
            //{
            //    Nodes[nodesUsed].Min = blasSpan[i].Min;
            //    Nodes[nodesUsed].Max = blasSpan[i].Max;
            //    Nodes[nodesUsed].LeafBlasIndex = (uint)i;
            //    Nodes[nodesUsed++].LeftAndRightBlasIndex = 0; // makes it a leaf
            //}
        }
    }
}
