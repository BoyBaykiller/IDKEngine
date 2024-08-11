using System;

namespace IDKEngine.Utils
{
    public static class Algorithms
    {
        /// <summary>
        /// Returns the first index in the array where array[index] >= value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arr"></param>
        /// <param name="value"></param>
        /// <param name="comparison"></param>
        /// <returns></returns>
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


        public delegate bool FuncLeftSide<T>(in T v);
        public static int Partition<T>(Span<T> arr, int start, int end, FuncLeftSide<T> func)
        {
            while (start < end)
            {
                ref T indices = ref arr[start];
                if (func(indices))
                {
                    start++;
                }
                else
                {
                    Swap(ref indices, ref arr[--end]);
                }
            }
            return start;
        }

        public static void PartialSort<T>(Span<T> arr, int first, int last, int sortRangeMin, int sortRangeMax, Comparison<T> comparison)
        {
            // Source: https://github.com/stephentoub/corefx/blob/a6aff797a33e606a60ec0c9ca034a161c609620f/src/System.Linq/src/System/Linq/OrderedEnumerable.cs#L590
            do
            {
                int i = first;
                int j = last - 1;
                T x = arr[i + ((j - i) >> 1)];
                do
                {
                    while (i < arr.Length && comparison(x, arr[i]) > 0)
                    {
                        i++;
                    }

                    while (j >= 0 && comparison(x, arr[j]) < 0)
                    {
                        j--;
                    }

                    if (i > j)
                    {
                        break;
                    }

                    if (i < j)
                    {
                        T temp = arr[i];
                        arr[i] = arr[j];
                        arr[j] = temp;
                    }

                    i++;
                    j--;
                }
                while (i <= j);

                if (sortRangeMin >= i)
                {
                    first = i + 1;
                }
                else if (sortRangeMax <= j)
                {
                    last = j - 1;
                }

                if (j - first <= last - i)
                {
                    if (first < j)
                    {
                        PartialSort(arr, first, j, sortRangeMin, sortRangeMax, comparison);
                    }

                    first = i;
                }
                else
                {
                    if (i < last)
                    {
                        PartialSort(arr, i, last, sortRangeMin, sortRangeMax, comparison);
                    }

                    last = j;
                }
            }
            while (first < last);
        }

        public static void Swap<T>(ref T a, ref T b)
        {
            (a, b) = (b, a);
        }
    }
}
