using System.Data.Common;
using NEventStore;

namespace EventSaucing.Projectors
{

    public interface IConventionalProjector
    {
        /// <summary>
        /// Gets or sets the last checkpoint the projector reached
        /// </summary>
        long LastCheckpoint { get; set; }
        /// <summary>
        /// Gets the name of the projector.  This can be the type's name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the a connection to the db where the projector will store the projection and its projection state state
        /// </summary>
        /// <returns></returns>
        DbConnection GetProjectionDb();
    }
}
