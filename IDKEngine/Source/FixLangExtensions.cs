using System;

namespace IDKEngine;

static class FixLangExtensions
{
    // https://github.com/dotnet/csharplang/discussions/9645
    extension(uint)
    {
        public static uint operator <<(uint value, uint shiftBy)
        {
            return value << (int)shiftBy;
        }

        public static uint operator >>(uint value, uint shiftBy)
        {
            return value >> (int)shiftBy;
        }
    }

    extension(int)
    {
        public static int operator >>(int value, uint shiftBy)
        {
            return value >> (int)shiftBy;
        }
    }

    // https://github.com/dotnet/runtime/issues/28070
    // TODO: Span<T> indexer that accepts uint https://github.com/dotnet/csharplang/issues/9856
}
