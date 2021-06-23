using System.Threading.Tasks;

namespace EventSaucing.Reactors {

    /// <summary>
    /// A repository pattern for loading and saving Reactors from the DB
    /// </summary>
    public interface IReactorRepository {
        /// <summary>
        /// Creates the reactor persistance tables.  Idempotent. You must call at least once per db instance buts its fine to call on application startup as it it idempotent
        /// </summary>
        /// <returns></returns>
        Task CreateReactorTablesAsync();

        /// <summary>
        /// Attaches a newly instantiated Reactor to the repository via the Unit Of Work pattern
        /// </summary>
        /// <param name="reactor"></param>
        /// <returns></returns>
        IUnitOfWork Attach(IReactor reactor);

        /// <summary>
        /// Loads the Reactor from the database and creates a Unit Of Work pattern for the reactor
        /// </summary>
        /// <param name="reactorId"></param>
        /// <returns></returns>
        Task<IUnitOfWork> LoadAsync(long reactorId);
    }
}