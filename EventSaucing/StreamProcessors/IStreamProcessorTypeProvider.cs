using System;
using System.Collections.Generic;
using Akka.Actor;

namespace EventSaucing.StreamProcessors {
    /// <summary>
    /// A contract which returns the Types of <see cref="StreamProcessor"/> to be used for stream processing.  You must implement this class and register it with Autofac.
    /// </summary>
    public interface IStreamProcessorPropsProvider {
        /// <summary>
        /// Returns all the Types of StreamProcessors which are scoped to a replica
        /// </summary>
        /// <returns></returns>
        IEnumerable<Props> GetReplicaScopedStreamProcessorsTypes();

        /// <summary>
        /// Returns all the Types which of StreamProcessors which are scoped to the cluster
        /// </summary>
        /// <returns></returns>
        IEnumerable<Props> GetClusterScopedStreamProcessorsTypes();
    }
}