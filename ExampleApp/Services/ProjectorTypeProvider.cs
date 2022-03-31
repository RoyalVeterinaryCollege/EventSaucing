using EventSaucing.Projectors;

namespace ExampleApp.Services
{
    public class ProjectorTypeProvider : IProjectorTypeProvider
    {
        public IEnumerable<Type> GetReplicaProjectorTypes() {
            return new List<Type>();
        }
    }
}
