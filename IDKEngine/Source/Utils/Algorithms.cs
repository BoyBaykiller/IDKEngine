using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IDKEngine.Utils;

public static class Algorithms
{
    public delegate bool Predicate<T>(in T v);

    public static uint FloatToKey(float value)
    {
        // Integer comparisons between numbers returned from this function behave
        // as if the original float values where compared.
        // Simple reinterpretation works only for [0, ...], but this also handles negatives

        //return Unsafe.BitCast<float, uint>(value);

        unchecked
        {
            // 1. Always flip the sign bit.
            // 2. If the sign bit was set, flip the other bits too.
            // Note: We do right shift on an int, meaning arithmetic shift

            uint f = Unsafe.BitCast<float, uint>(value);
            uint mask = (uint)((int)f >> 31 | (1 << 31));

            return f ^ mask;
        }
    }

    public static float KeyToFloat(uint key)
    {
        unchecked
        {
            uint mask = ((key >> 31) - 1) | 0x80000000;
            return Unsafe.BitCast<uint, float>(key ^ mask);
        }
    }

    public static unsafe void RadixSort<T>(Span<T> input, Span<T> output, Func<T, uint> getKey)
    {
        // Out performs built-in sort except for small inputs (~64)
        // http://stereopsis.com/radix.html

        Debug.Assert(output.Length >= input.Length);

        const int radixSize = 11;
        const int binSize = 1 << radixSize;
        const int mask = binSize - 1;
        const int passes = 3;

        // We don't use Span<int> here because:
        // 1. Not all bounds check are elided: https://github.com/dotnet/runtime/issues/112725
        // 2. Constant offsets are not baked into address calculation: https://discord.com/channels/143867839282020352/312132327348240384/1342254292995801100
        // 3. Local functions can't capture Span<T>: https://discord.com/channels/143867839282020352/312132327348240384/1336514607493283881
        int* prefixSum = stackalloc int[binSize * passes];
        
        // Compute histogram for all passes
        for (int i = 0; i < input.Length; i++)
        {
            uint key = getKey(input[i]);

            GetPrefixSumRef(key, 0)++;
            GetPrefixSumRef(key, 1)++;
            GetPrefixSumRef(key, 2)++;
        }

        // Compute prefix sum for all passes
        {
            int sum0 = 0, sum1 = 0, sum2 = 0;
            for (int i = 0; i < binSize; i++)
            {
                int temp0 = prefixSum[i + 0u * binSize];
                int temp1 = prefixSum[i + 1u * binSize];
                int temp2 = prefixSum[i + 2u * binSize];

                prefixSum[i + 0u * binSize] = sum0;
                prefixSum[i + 1u * binSize] = sum1;
                prefixSum[i + 2u * binSize] = sum2;

                sum0 += temp0;
                sum1 += temp1;
                sum2 += temp2;
            }
        }

        // Sort from LSB to MSB in radix-sized steps
        for (int i = 0; i < passes; i++)
        {
            int j = 0;
            for (; j < input.Length - 3; j += 4)
            {
                T t0 = input[j + 0];
                uint key0 = getKey(t0);
                output[GetPrefixSumRef(key0, i)++] = t0;

                T t1 = input[j + 1];
                uint key1 = getKey(t1);
                output[GetPrefixSumRef(key1, i)++] = t1;

                T t2 = input[j + 2];
                uint key2 = getKey(t2);
                output[GetPrefixSumRef(key2, i)++] = t2;

                T t3 = input[j + 3];
                uint key3 = getKey(t3);
                output[GetPrefixSumRef(key3, i)++] = t3;
            }
            for (; j < input.Length; j++)
            {
                T t0 = input[j];
                uint key0 = getKey(t0);
                output[GetPrefixSumRef(key0, i)++] = t0;
            }

            Swap(ref input, ref output);
        }
        
        ref int GetPrefixSumRef(uint key, int pass)
        {
            uint radix = (key >> (pass * radixSize)) & mask;
            ref int offset = ref prefixSum[radix + pass * binSize];

            return ref offset;
        }
    }

    /// <summary>
    /// Searches for the first element in the array which is not ordered before value. Runs in O(log N).
    /// Input should be sorted. Equivalent to std::lower_bound
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="arr"></param>
    /// <param name="value"></param>
    /// <param name="comparison"></param>
    /// <returns>The first index in the array where array[index] >= value, or arr.Length if no such value is found</returns>
    public static int SortedLowerBound<T>(ReadOnlySpan<T> arr, in T value, Comparison<T> comparison)
    {
        int lo = 0;
        int hi = arr.Length - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (comparison(arr[mid], value) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (comparison(arr[lo], value) < 0)
        {
            lo++;
        }

        return lo;
    }

    /// <summary>
    /// Input should be sorted. Removes all items except one from every group of consecutive equivalent items.
    /// Equivalent to std::unique
    /// </summary>
    /// <param name="arr"></param>
    /// <returns></returns>
    public static int SortedFilterDuplicates(Span<int> arr)
    {
        if (arr.Length == 0)
        {
            return 0;
        }

        int lastNewItem = arr[0];
        int uniqueItemCounter = 1;
        for (int i = 1; i < arr.Length; i++)
        {
            int item = arr[i];
            if (lastNewItem != item)
            {
                arr[uniqueItemCounter++] = item;
                lastNewItem = item;
            }
        }

        return uniqueItemCounter;
    }

    public static void PartialSort<T>(Span<T> values, int sortEnd, Random rng, Comparison<T> comparison, Action<int, int> onSwap = null)
    {
        PartialSort(values, 0, values.Length, sortEnd, rng, comparison, onSwap);
    }

    /// <summary>
    /// Rearranges elements such that the range [start, end) contains the sorted (end − start) smallest elements in the range [start, end).
    /// The order of equal elements is not guaranteed to be preserved.
    /// The order of the unsorted elements in the range [middle, last) is unspecified and depending on the rng undeterministic.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="sortEnd"></param>
    /// <param name="comparison"></param>
    private static void PartialSort<T>(Span<T> values, int start, int end, int sortEnd, Random rng, Comparison<T> comparison, Action<int, int> onSwap = null)
    {
        // Swap randomly selected pivot with start
        SwapNotifyUser(values, rng.Next(start, end - 1), start, onSwap);
        T pivot = values[start];

        int middle = Partition(values, start, end, (in T it) => comparison(it, pivot) < 0, onSwap);

        // The pivot is now located at the end by the partitioning step
        // We swap it into middle. It will divide the next two recursive sorts and gets excluded from both
        // That way we avoid avoid all values ending up in one partition causing endless loop
        SwapNotifyUser(values, middle, end - 1, onSwap);

        int lSortEnd = middle;
        int rSortStart = middle + 1;

        if ((lSortEnd - start) > 1)
        {
            PartialSort(values, start, lSortEnd, sortEnd, rng, comparison, onSwap);
        }
        if ((end - rSortStart) > 1 && rSortStart < sortEnd)
        {
            PartialSort(values, rSortStart, end, sortEnd, rng, comparison, onSwap);
        }
    }

    public static int Partition<T>(Span<T> arr, int start, int end, Predicate<T> func, Action<int, int> onSwap = null)
    {
        arr = arr.Slice(start, end - start);
        return start + Partition(arr, func, onSwap);
    }

    /// <summary>
    /// Reorders the elements in such a way that all elements for which the predicate returns true
    /// precede all elements for which the predicate returns false. Relative order of the elements is not preserved.
    /// Equivalent to std::partition
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="arr"></param>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="func"></param>
    /// <returns>Index to the first element of the second group.</returns>
    public static int Partition<T>(Span<T> arr, Predicate<T> func, Action<int, int> onSwap = null)
    {
        int start = 0;
        int end = arr.Length;

        while (start < end)
        {
            ref T value = ref arr[start];
            if (func(value))
            {
                start++;
            }
            else
            {
                SwapNotifyUser(arr, start, --end, onSwap);
            }
        }

        return start;
    }

    /// <summary>
    /// Reorders the integers in such a way that all elements for which bitArray[arr[i]]==true
    /// precede the integers for which it is not. Relative order of the elements is preserved.
    /// Auxiliary must be able to hold as many elements as bitArray has false entries
    /// Equivalent to std::stable_partition
    /// </summary>
    /// <param name="source"></param>
    /// <param name="auxiliary"></param>
    /// <param name="bitArray"></param>
    /// <returns></returns>
    public static int StablePartition(Span<int> source, Span<int> auxiliary, bool[] bitArray)
    {
        int lCounter = 0;
        int rCounter = 0;

        for (int i = 0; i < source.Length; i++)
        {
            int id = source[i];
            if (bitArray[id])
            {
                source[lCounter++] = id;
            }
            else
            {
                auxiliary[rCounter++] = id;
            }
        }

        auxiliary.Slice(0, rCounter).CopyTo(source.Slice(lCounter, rCounter));

        return lCounter;
    }

    public static void Swap<T>(ref T a, ref T b) where T : allows ref struct
    {
        T temp = a;
        a = b;
        b = temp;
    }

    private static void SwapNotifyUser<T>(Span<T> values, int idA, int idB, Action<int, int> onSwap = null)
    {
        Swap(ref values[idA], ref values[idB]);
        onSwap?.Invoke(idA, idB);
    }

    public static void SetBit(ref int value, int n, bool x)
    {
        value = (value & ~(1 << n)) | (Convert.ToInt32(x) << n);
    }
}
