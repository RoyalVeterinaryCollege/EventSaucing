using System.Collections.Generic;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    public interface IReactorRepository {

        IUnitOfWork Attach(IReactor reactor);
        Task<IUnitOfWork> LoadAsync(long reactorId);
      
    }
}