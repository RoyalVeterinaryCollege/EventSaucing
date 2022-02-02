using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace EventSaucing.Reactors {
    public static class Extensions {
        /// <summary>
        /// Gets the EventSaucing:Bucket name from config
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static string GetLocalBucketName(this IConfiguration config) {
            var bucket = config["EventSaucing:Bucket"];
            if (string.IsNullOrWhiteSpace(bucket))
                throw new ArgumentException("EventSaucing:Bucket not set in config.");
            return bucket;
        }

        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random rng) {
            T[] elements = source.ToArray();
            // Note i > 0 to avoid final pointless iteration
            for (int i = elements.Length - 1; i > 0; i--) {
                // Swap element "i" with a random earlier element it (or itself)
                int swapIndex = rng.Next(i + 1);
                T tmp = elements[i];
                elements[i] = elements[swapIndex];
                elements[swapIndex] = tmp;
            }
            // Lazily yield (avoiding aliasing issues etc)
            foreach (T element in elements) {
                yield return element;
            }
        }
    }
}
