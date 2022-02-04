using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit3;
using NUnit.Framework;

namespace EventSaucing.EventStream
{
    [TestFixture]
    public abstract class LocalEventStreamActorTests : TestKit {
        /// <summary>
        /// LocalEventStreamActor
        /// </summary>
        protected IActorRef sut;
        /// <summary>
        /// TestProbe which represents the child EventStorePoller actor
        /// </summary>
        protected TestProbe _pollEventStoreProbe;
        /// <summary>
        /// Test Probe which is subscribed to OrderedCommitNotification messages on the EventBus 
        /// </summary>
        private TestProbe _eventBusStoreProbe;

        public LocalEventStreamActorTests() {
            // create a subscription for OrderedCommitNotification
            _eventBusStoreProbe = CreateTestProbe();
            Sys.EventStream.Subscribe(_eventBusStoreProbe, typeof(OrderedCommitNotification));

            //inject dependencies
            var cache = new InMemoryCommitSerialiserCache(10);
            _pollEventStoreProbe = CreateTestProbe();
            Func<IUntypedActorContext, IActorRef> maker = (ctx) => _pollEventStoreProbe.Ref;
            sut = Sys.ActorOf(Props.Create<LocalEventStreamActor>(cache, maker));


           Because();
        }

        protected virtual void Because() { }

    }

    public class When_parp  : LocalEventStreamActorTests
    {
        private FakeCommit _commit1;

        protected override void Because() {


            _commit1 = new FakeCommit() { CheckpointToken = 10L };
            sut.Tell(new CommitNotification(_commit1), this.TestActor);
        }

        [Test]
        public void When_() {
            _pollEventStoreProbe.ExpectMsg<OrderedCommitNotification>(
                x => x.Commit == _commit1
                , TimeSpan.FromSeconds(1));
        }

    }
}
