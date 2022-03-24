using EventSaucing.Projectors;

namespace ExampleApp.Services
{
    public class ProjectorTypeProvider : IProjectorTypeProvider
    {
        public IEnumerable<Type> GetProjectorTypes() {
            return new List<Type>();
        }
    }
}
