using EventSaucing.StreamProcessors;

namespace ExampleApp.Services
{
    public class StreamProcessorTypeProvider : IStreamProcessorTypeProvider
    {
        public IEnumerable<Type> GetProjectorTypes() {
            return new List<Type>();
        }
    }
}
