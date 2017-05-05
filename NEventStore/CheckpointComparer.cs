using System.Collections.Generic;
using Scalesque;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// Comparers checkpoints.  None is less then any checkpoint except another None and in this case they are equal
    /// </summary>
    class CheckpointComparer : IComparer<Option<long>> {
        /// <summary>
        /// Is x less than or equal to y
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public int Compare(Option<long> x, Option<long> y) {
            if (x.IsEmpty && y.IsEmpty) return 0;
            else if (x.IsEmpty && y.HasValue) return -1;
            else if (x.HasValue && y.IsEmpty) return 1;
            else return (int)(x.Get() - y.Get());
        }
    }
}