using OpenTK.Graphics.OpenGL;

namespace BBOpenGL;

public static partial class BBG
{
    public class TimerQuery : IDisposable
    {
        public float ElapsedMilliseconds { get; private set; }
        public readonly int ID;
        public TimerQuery()
        {
            GL.CreateQueries(QueryTarget.TimeElapsed, 1, ref ID);
        }

        public static TimerQuery StartNew()
        {
            TimerQuery timer = new TimerQuery();
            timer.Start();

            return timer;
        }

        public void Start()
        {
            GL.BeginQuery(QueryTarget.TimeElapsed, ID);
        }

        public void Stop()
        {
            GL.EndQuery(QueryTarget.TimeElapsed);

            GL.GetQueryObjecti64(ID, QueryObjectParameterName.QueryResult, out long resultNanoSec);
            ElapsedMilliseconds = resultNanoSec / 1000000.0f;
        }

        public void Dispose()
        {
            GL.DeleteQueries(1, in ID);
        }
    }
}
