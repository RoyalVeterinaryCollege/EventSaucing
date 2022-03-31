using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EventSaucing.Projectors {
    /// <summary>
    /// A contract which returns the Types of projectors to be used for projection
    /// </summary>
    public interface IProjectorTypeProvider {
        /// <summary>
        /// Returns all the Types which are the Types of replica Projectors
        /// </summary>
        /// <returns></returns>
        IEnumerable<Type> GetReplicaProjectorTypes();

        /// <summary>
        /// Returns all the Types which are the Types of Stream Processors Projectors
        /// </summary>
        /// <returns></returns>
        IEnumerable<Type> GetStreamProcessorTypes();
    }

    /// <summary>
    /// An implementation of IProjectorTypeProvider which searches the entry assembly for classes tagged with ProjectorAttribute
    /// </summary>
    public class EntryAssemblyProjectorTypeProvider : IProjectorTypeProvider {
        public IEnumerable<Type> GetReplicaProjectorTypes() {
            //Reflect on assembly to identify projectors and have DI create them
            var types = Assembly.GetEntryAssembly().GetTypes();
            return
                from type in types
                    where type.GetCustomAttributes(typeof(ProjectorAttribute), false).Any()
                    select type;
        }
    }
}