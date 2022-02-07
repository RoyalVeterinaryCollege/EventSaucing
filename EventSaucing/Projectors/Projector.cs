using System.Threading.Tasks;
using Akka.Actor;
using EventSaucing.EventStream;

namespace EventSaucing.Projectors {
    public abstract class Projector : ReceiveActor {

        public Projector() {

            ReceiveAsync<CatchUpMessage>(ReceivedAsync);
            ReceiveAsync<OrderedCommitNotification>(ReceivedAsync);
        }

        protected abstract Task ReceivedAsync(OrderedCommitNotification msg);

        protected abstract Task ReceivedAsync(CatchUpMessage arg);
    }
}