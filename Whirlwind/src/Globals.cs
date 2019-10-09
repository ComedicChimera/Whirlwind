﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Whirlwind
{
    static class WhirlGlobals
    {
        // global variables
        public static string WHIRL_PATH;

        // global functions
        public static bool DictionaryEquals<TKey, TValue>(
            this IDictionary<TKey, TValue> first, IDictionary<TKey, TValue> second)
        {
            if (first == second)
                return true;
            else if (first == null || second == null)
                return false;
            else if (first.Count != second.Count)
                return false;

            foreach (var item in first)
            {
                if (!second.Any(x => x.Key.Equals(item.Key)))
                    return false;

                if (!item.Value.Equals(second.Where(x => x.Key.Equals(item.Key)).First().Value))
                    return false;
            }

            return true;
        }

        // SequenceEqual was really giving me grief so I made my own implementation ;)
        public static bool EnumerableEquals<T>(
            this IEnumerable<T> first, IEnumerable<T> second)
        {
            if (first == second)
                return true;
            else if (first == null || second == null)
                return false;

            using (var e1 = first.GetEnumerator())
            using (var e2 = second.GetEnumerator())
            {
                while (e1.MoveNext() && e2.MoveNext())
                {
                    if (!e1.Current.Equals(e2.Current))
                        return false;
                }

                return !e1.MoveNext() && !e2.MoveNext();
            }
        }

        public static void RemoveLast<T>(
            this List<T> self)
        {
            self.RemoveAt(self.Count - 1);
        }
    }
}