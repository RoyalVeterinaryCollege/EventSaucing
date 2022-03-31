using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EventSaucing.StreamProcessors.Projectors;

namespace EventSaucing.StreamProcessors {
    /// <summary>
    /// A contract which returns the Types of <see cref="StreamProcessor"/> to be used for stream processing.  Returns both Projectors and Reactors
    /// </summary>
    public interface IStreamProcessorTypeProvider {
        /// <summary>
        /// Returns all the Types which are the Types of replica Projectors
        /// </summary>
        /// <returns></returns>
        IEnumerable<Type> GetReplicaProjectorTypes();

        /// <summary>
        /// Returns all the Types which are the Types of Stream Processors Projectors
        /// </summary>
        /// <returns></returns>
        //IEnumerable<Type> GetStreamProcessorTypes();
    }

    /// <summary>
    /// An implementation of IProjectorTypeProvider which searches the entry assembly for classes tagged with ProjectorAttribute
    /// </summary>
    public class EntryAssemblyStreamProcessorTypeProvider : IStreamProcessorTypeProvider {
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