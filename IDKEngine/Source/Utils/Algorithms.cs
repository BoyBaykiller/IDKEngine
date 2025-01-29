using System;
using System.Collections;

namespace IDKEngine.Utils
{
    public static class Algorithms
    {
        public delegate bool Predicate<T>(in T v);

        /// <summary>
        /// Searches for the first element in the array which is not ordered before value. Runs in O(log N).
        /// Equivalent to std::lower_bound
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arr"></param>
        /// <param name="value"></param>
        /// <param name="comparison"></param>
        /// <returns>The first index in the array where array[index] >= value, or arr.Length if no such value is found</returns>
        public static int BinarySearchLowerBound<T>(ReadOnlySpan<T> arr, in T value, Comparison<T> comparison)
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

        public static void PartialSort<T>(Span<T> values, int sortEnd, Random rng, Comparison<T> comparison, Action<int, int> onSwap = null)
        {
            PartialSort(values, 0, values.Length, sortEnd, rng, comparison, onSwap);
        }

        /// <summary>
        /// Rearranges elements such that the range [start, end) contains the sorted (end − start) smallest elements in the range [start, end).
        /// The order of equal elements is not guaranteed to be preserved. The order of the remaining elements in the range [middle, last) is unspecified.
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
            return Partition(arr, func, onSwap) + start;
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
        /// Reorders the integers in such a way that all elements for which bitArray[arr[i]] is true
        /// precede the integers for which it is false. Relative order of the elements is preserved.
        /// Equivalent to std::stable_partition
        /// </summary>
        /// <param name="arr"></param>
        /// <param name="auxiliary"></param>
        /// <param name="bitArray"></param>
        /// <returns></returns>
        public static int StablePartition(Span<int> arr, Span<int> auxiliary, BitArray bitArray)
        {
            int lCounter = 0;
            int rCounter = 0;

            for (int i = 0; i < arr.Length; i++)
            {
                int id = arr[i];
                if (bitArray[id])
                {
                    arr[lCounter++] = id;
                }
                else
                {
                    auxiliary[rCounter++] = id;
                }
            }

            Memory.CopyElements(ref auxiliary[0], ref arr[lCounter], arr.Length - lCounter);

            return lCounter;
        }

        public static void Swap<T>(ref T a, ref T b)
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
}
