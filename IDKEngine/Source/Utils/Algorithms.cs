using System;

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

        public static void PartialSort<T>(Span<T> values, int sortEnd, Comparison<T> comparison)
        {
            PartialSort(values, 0, values.Length, sortEnd, comparison);
        }

        /// <summary>
        /// Rearranges elements such that the range [start, end) contains the sorted (sortEnd − first) smallest elements in the range [start, end).
        /// The order of equal elements is not guaranteed to be preserved. The order of the remaining elements in the range [middle, last) is unspecified.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="values"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="sortEnd"></param>
        /// <param name="comparison"></param>
        public static void PartialSort<T>(Span<T> values, int start, int end, int sortEnd, Comparison<T> comparison)
        {
            T pivot = values[(start + end) / 2]; // Consider randomizing
            int middle1 = Partition(values, start, end, (in T it) => comparison(it, pivot) < 0);
            int middle2 = Partition(values, middle1, end, (in T it) => !(comparison(pivot, it) < 0));

            if ((middle1 - start) >= 2)
            {
                PartialSort(values, start, middle1, sortEnd, comparison);
            }
            if ((end - middle2) >= 2 && middle2 < sortEnd)
            {
                PartialSort(values, middle2, end, sortEnd, comparison);
            }
        }

        public static int Partition<T>(Span<T> arr, int start, int end, Predicate<T> func)
        {
            Span<T> values = arr.Slice(start, end - start);
            return Partition(values, func) + start;
        }

        /// <summary>
        /// Reorders the elements in the range [start, end) in such a way that all elements for which the predicate returns true
        /// precede all elements for which the predicate returns false. Relative order of the elements is not preserved.
        /// Equivalent to std::partition
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arr"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="func"></param>
        /// <returns>Index to the first element of the second group.</returns>
        public static int Partition<T>(Span<T> arr, Predicate<T> func)
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
                    Swap(ref value, ref arr[--end]);
                }
            }

            return start;
        }

        /// <summary>
        /// Reorders the elements in the range [first, last) in such a way that all elements for which the predicate returns true
        /// precede the elements for which predicate p returns false. Relative order of the elements is preserved.
        /// Equivalent to std::stable_partition
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arr"></param>
        /// <param name="func"></param>
        public static void StablePartition<T>(Span<T> arr, Predicate<T> func)
        {
            T[] leftTemp = new T[arr.Length];
            T[] rightTemp = new T[arr.Length];

            int leftCounter = 0;
            int rightCounter = 0;

            for (int i = 0; i < arr.Length; i++)
            {
                ref T value = ref arr[i];
                if (func(value))
                {
                    leftTemp[leftCounter++] = value;
                }
                else
                {
                    rightTemp[rightCounter++] = value;
                }
            }

            for (int i = 0; i < leftCounter; i++)
            {
                arr[i] = leftTemp[i];
            }

            for (int i = 0; i < rightCounter; i++)
            {
                arr[leftCounter + i] = rightTemp[i];
            }
        }

        public static void Swap<T>(ref T a, ref T b)
        {
            (a, b) = (b, a);
        }
    }
}
