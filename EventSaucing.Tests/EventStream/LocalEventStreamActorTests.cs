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
        protected TestProbe _eventBusStoreProbe;

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

    public class When_receives_first_commit  : LocalEventStreamActorTests
    {
        private FakeCommit _commit1;

        protected override void Because() {
            _commit1 = new FakeCommit() { CheckpointToken = 10L };
            sut.Tell(new CommitNotification(_commit1), this.TestActor);
        }

        [Test] public void Should_not_stream_the_commit_because_it_cant_be_ordered_as_we_dont_know_the_earlier_commit() {
            _eventBusStoreProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100)); 
            /*ExpectMsg<OrderedCommitNotification>(
                x => x.Commit == _commit1
                , TimeSpan.FromSeconds(1));*/
        }

        [Test]
        public void Should_not_poll_the_event_store(){
            _pollEventStoreProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }
    }
    public class When_receives_second_commit_which_follows_first : LocalEventStreamActorTests
    {
        private FakeCommit _commit1;
        private FakeCommit _commit2;


        protected override void Because()
        {
            _commit1 = new FakeCommit() { CheckpointToken = 10L };
            _commit2 = new FakeCommit() { CheckpointToken = 11L };

            sut.Tell(new CommitNotification(_commit1), this.TestActor);
            sut.Tell(new CommitNotification(_commit2), this.TestActor);
        }

        [Test]
        public void Should_stream_second_commit() {
            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification> (x=>
                x.Commit.CheckpointToken == _commit2.CheckpointToken && 
                x.PreviousCheckpoint.Get()==_commit1.CheckpointToken,
                TimeSpan.FromMilliseconds(1000)
                );
        }

        [Test]
        public void Should_not_poll_the_event_store() {
            _pollEventStoreProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }
    }
}
