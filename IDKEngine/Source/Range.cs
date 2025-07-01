using System;

namespace IDKEngine;

public record struct Range
{
    public int Start;
    public int Count;

    public int End
    {
        readonly get
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

    public readonly bool Contains(int index)
    {
        return index >= Start && index < End;
    }

    public readonly bool Overlaps(Range range, out Range overlap)
    {
        overlap = new Range();
        overlap.Start = Math.Max(Start, range.Start);
        overlap.End = Math.Min(End, range.End);
        return overlap.Count > 0;
    }

    public readonly bool Overlaps(Range range, out int overlap)
    {
        bool overlaps = Overlaps(range, out Range overlappingRange);
        overlap = overlappingRange.Count;
        return overlaps;
    }
}
