using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    class TimerQuery
    {
        public float MeasuredMilliseconds { get; private set; }
        public int ID;
        private int readyForNext;
        public TimerQuery()
        {
            GL.CreateQueries(QueryTarget.TimeElapsed, 1, out ID);
            readyForNext = 1;
        }

        public void Start()
        {
            if (readyForNext != 0)
            {
                GL.BeginQuery(QueryTarget.TimeElapsed, ID);
            }
        }

        public void End()
        {
            if (readyForNext != 0)
            {
                GL.EndQuery(QueryTarget.TimeElapsed);
            }

            GL.GetQueryObject(ID, GetQueryObjectParam.QueryResultAvailable, out readyForNext);
            if (readyForNext != 0)
            {
                GL.GetQueryObject(ID, GetQueryObjectParam.QueryResult, out long resultNanoSec);
                MeasuredMilliseconds = resultNanoSec / 1000000.0f;
            }
        }
    }
}
