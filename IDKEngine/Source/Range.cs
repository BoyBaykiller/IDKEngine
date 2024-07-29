namespace IDKEngine
{
    public record struct Range
    {
        public int Start;
        public int Count;

        public int End
        {
            get
            {
                return Start + Count;
            }

            set
            {
                Count = value - Start;
            }
        }

        public Range(int start, int count)
        {
            Start = start;
            Count = count;
        }
    }
}
