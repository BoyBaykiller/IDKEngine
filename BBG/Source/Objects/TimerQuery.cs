using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public class TimerQuery : IDisposable
        {
            public float MeasuredMilliseconds { get; private set; }
            public readonly int ID;
            public TimerQuery()
            {
                GL.CreateQueries(QueryTarget.TimeElapsed, 1, ref ID);
            }

            public static TimerQuery BeginNew()
            {
                TimerQuery timer = new TimerQuery();
                timer.Begin();

                return timer;
            }

            public void Begin()
            {
                GL.BeginQuery(QueryTarget.TimeElapsed, ID);
            }

            public void End()
            {
                GL.EndQuery(QueryTarget.TimeElapsed);

                GL.GetQueryObjecti64(ID, QueryObjectParameterName.QueryResult, out long resultNanoSec);
                MeasuredMilliseconds = resultNanoSec / 1000000.0f;
            }

            public void Dispose()
            {
                GL.DeleteQueries(1, in ID);
            }
        }
    }
}
