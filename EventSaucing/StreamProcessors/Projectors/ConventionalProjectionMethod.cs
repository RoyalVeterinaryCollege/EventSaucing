using System.Data;
using System.Threading.Tasks;
using NEventStore;

namespace EventSaucing.StreamProcessors.Projectors
{
    /// <summary>
    /// A delegate describing the signature of a method which projects an event
    /// </summary>
    /// <param name="tx"></param>
    /// <param name="commit"></param>
    /// <param name="event"></param>
    public delegate Task ConventionalProjectionMethodAsync(IDbTransaction tx, ICommit commit, object @event);
}
