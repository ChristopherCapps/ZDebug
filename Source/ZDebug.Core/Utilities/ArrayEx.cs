﻿using System;

namespace ZDebug.Core.Utilities
{
    internal static class ArrayEx
    {
        public static T[] Create<T>(int length, Func<int, T> createItem = null)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException("length");
            }

            if (length == 0)
            {
                return new T[0];
            }

            var result = new T[length];

            if (createItem != null)
            {
                for (int i = 0; i < length; i++)
                {
                    result[i] = createItem(i);
                }
            }

            return result;
        }

        public static T[] Empty<T>()
        {
            return new T[0];
        }

        public static T[] Resize<T>(this T[] array, int newLength)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (newLength < 0)
            {
                throw new ArgumentOutOfRangeException("newLength");
            }

            var newArray = new T[newLength];

            if (newLength > 0)
            {
                var numberToCopy = Math.Min(array.Length, newLength);
                Array.Copy(array, 0, newArray, 0, numberToCopy);
            }

            return newArray;
        }

        public static TResult[] Select<T, TResult>(this T[] array, Func<T, TResult> selector)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (array.Length == 0)
            {
                return ArrayEx.Empty<TResult>();
            }

            if (selector == null)
            {
                throw new ArgumentNullException("selector");
            }

            var result = new TResult[array.Length];

            for (int i = 0; i < array.Length; i++)
            {
                result[i] = selector(array[i]);
            }

            return result;
        }
    }
}