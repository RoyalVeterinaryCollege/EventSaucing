using System;
using System.Collections.Generic;
using EventSaucing.Reactors;

namespace EventSaucing.Projectors {
    /// <summary>
    /// An implementation of IProjectorTypeProvider which adds <see cref="ReactorAggregateSubscriptionProjector"></see> and all types added by <see cref="EntryAssemblyProjectorTypeProvider"></see>
    /// </summary>
    public class DefaultProjectorTypeProvider : IProjectorTypeProvider {
        public IEnumerable<Type> GetProjectorTypes() {
            var types = new List<Type> {typeof(ReactorAggregateSubscriptionProjector)};
            types.AddRange(new EntryAssemblyProjectorTypeProvider().GetProjectorTypes());
            return types;
        }
    }
}