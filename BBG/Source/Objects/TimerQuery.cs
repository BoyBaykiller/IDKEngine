using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public class TimerQuery : IDisposable
        {
            public float MeasuredMilliseconds { get; private set; }
            public int ID;
            public TimerQuery()
            {
                GL.CreateQueries(QueryTarget.TimeElapsed, 1, ref ID);
            }

            public void Begin()
            {
                GL.BeginQuery(QueryTarget.TimeElapsed, ID);
            }

            public void End()
            {
                GL.EndQuery(QueryTarget.TimeElapsed);

                long resultNanoSec = 0;
                GL.GetQueryObjecti64(ID, QueryObjectParameterName.QueryResult, ref resultNanoSec);
                MeasuredMilliseconds = resultNanoSec / 1000000.0f;
            }

            public void Dispose()
            {
                GL.DeleteQuery(ID);
            }
        }
    }
}
