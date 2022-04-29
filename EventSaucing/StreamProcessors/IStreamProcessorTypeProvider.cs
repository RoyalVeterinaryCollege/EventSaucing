using System;
using System.Collections.Generic;
using Akka.Actor;

namespace EventSaucing.StreamProcessors {
    /// <summary>
    /// Cluster-scoped StreamProcessors can move between Akka nodes.  This limits that movement to only the nodes which have the role the Actor needs.
    /// </summary>
    public class ClusterStreamProcessorInitialisation {
        public ClusterStreamProcessorInitialisation(Props props, string clusterRole) {
            Props = props;
            ClusterRole = clusterRole;
        }

        /// <summary>
        /// Props of the actor
        /// </summary>
        public Props Props { get; }
        /// <summary>
        /// The role of the Akka node which can host your singleton actor
        /// </summary>
        public string ClusterRole { get;  }

    }
    /// <summary>
    /// A contract which returns the initialisation parameters for <see cref="StreamProcessor"/> to be used for stream processing.  You must implement this class and register it with Autofac.
    /// </summary>
    public interface IStreamProcessorInitialisation {
        /// <summary>
        /// Returns all the Props of StreamProcessors which are scoped to a replica
        /// </summary>
        /// <returns></returns>
        IEnumerable<Props> GetReplicaScopedStreamProcessorProps();

        /// <summary>
        /// Returns all the ClusterStreamProcessorInitialisation which of StreamProcessors which are scoped to the cluster
        /// </summary>
        /// <returns></returns>
        IEnumerable<ClusterStreamProcessorInitialisation> GetClusterScopedStreamProcessorsInitialisationParameters();
    }
}