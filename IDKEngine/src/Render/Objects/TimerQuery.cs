using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    class TimerQuery : IDisposable
    {
        public float MeasuredMilliseconds { get; private set; }
        public int ID;
        public TimerQuery()
        {
            GL.CreateQueries(QueryTarget.TimeElapsed, 1, out ID);
        }

        public void Begin()
        {
            GL.BeginQuery(QueryTarget.TimeElapsed, ID);
        }

        public void End()
        {
            GL.EndQuery(QueryTarget.TimeElapsed);
            GL.GetQueryObject(ID, GetQueryObjectParam.QueryResult, out long resultNanoSec);
            MeasuredMilliseconds = resultNanoSec / 1000000.0f;
        }

        public void Dispose()
        {
            GL.DeleteQuery(ID);
        }
    }
}
