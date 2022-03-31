using EventSaucing.StreamProcessors;

namespace ExampleApp.Services
{
    public class StreamProcessorTypeProvider : IStreamProcessorTypeProvider
    {
        public IEnumerable<Type> GetReplicaProjectorTypes() {
            return new List<Type>();
        }
    }
}
