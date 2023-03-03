using System;
using System.Collections.Generic;
using Akka.Actor;
using Scalesque;

namespace EventSaucing.StreamProcessors {

    /// <summary>
    /// A Replica-scoped stream processor.  These remain inside the process that starts them and don't migrate to different shards.
    /// </summary>
    public class ReplicaStreamProcessorInitialisation {
        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="props">Props of the actor</param>
        /// <param name="actorName">Name of the actor inside the ActorSystem.</param>
        public ReplicaStreamProcessorInitialisation(Props props, string actorName) {
            Props = props;
            ActorName = actorName;
        }

        public Props Props { get; }
        public string ActorName { get; }
    }

    /// <summary>
    /// Cluster-scoped StreamProcessors can move between Akka nodes.  This limits that movement to only the nodes which have the role the Actor needs.
    /// </summary>
    public class ClusterStreamProcessorInitialisation : ReplicaStreamProcessorInitialisation {
        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="props">Props of the actor</param>
        /// <param name="actorName">Name of the actor inside the ActorSystem. Not optional for cluster-scoped SPs and ignored for replica-scoped SPs</param>
        /// <param name="clusterRole">The role of the Akka node which can host your singleton actor</param>
        public ClusterStreamProcessorInitialisation(Props props, string actorName, string clusterRole):base(props, actorName) {
            ClusterRole = clusterRole;
        }
        public string ClusterRole { get;  }
    }

    
    /// <summary>
    /// A contract which returns the initialisation parameters for <see cref="StreamProcessor"/> to be used for stream processing.  You must implement this class and register it with Autofac.
    /// </summary>
    public interface IStreamProcessorInitialisation {
        /// <summary>
        /// Returns all the ReplicaStreamProcessorInitialisations for StreamProcessors which are scoped to a replica
        /// </summary>
        /// <returns></returns>
        IEnumerable<ReplicaStreamProcessorInitialisation> GetReplicaScopedStreamProcessorProps();

        /// <summary>
        /// Returns all the ClusterStreamProcessorInitialisation which of StreamProcessors which are scoped to the cluster
        /// </summary>
        /// <returns></returns>
        IEnumerable<ClusterStreamProcessorInitialisation> GetClusterScopedStreamProcessorsInitialisationParameters();
    }
}