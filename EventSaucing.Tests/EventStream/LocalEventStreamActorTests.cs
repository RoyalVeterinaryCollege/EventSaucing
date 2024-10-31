using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
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
            sut = Sys.ActorOf(Props.Create<LocalEventStreamActor>(cache, maker, new NullLogger<LocalEventStreamActor>()));


            Because();
        }


        protected virtual void Because() { }

    }

    public class When_starts : LocalEventStreamActorTests{
        [Test]
        public void Should_poll_the_event_store_to_find_head(){
            _pollEventStoreProbe.ExpectMsg<EventStorePollerActor.Messages.SendHeadCommit>(duration:TimeSpan.FromMilliseconds(100));
        }
    }

    public class When_receives_first_ordered_commit_from_poller : LocalEventStreamActorTests
    {
        private OrderedCommitNotification _commit1;

        protected override void Because() {
            _commit1 = new OrderedCommitNotification(new FakeCommit{CheckpointToken = 10L}, previousCheckpoint:9L);
            sut.Tell(_commit1, this.TestActor);
        }

        [Test] public void Should_stream_the_commit() {
            _eventBusStoreProbe.ExpectMsg(_commit1, TimeSpan.FromMilliseconds(100)); 
        }
    }
    public class When_receives_second_commit_which_follows_first : LocalEventStreamActorTests {
        private FakeCommit _commit1;
        private FakeCommit _commit2;
        protected override void Because()  {
            _commit1 = new FakeCommit{ CheckpointToken = 10L };
            _commit2 = new FakeCommit{ CheckpointToken = 11L };

            sut.Tell(new OrderedCommitNotification(_commit1, 9L), this.TestActor);
            sut.Tell(new CommitNotification(_commit2), this.TestActor);

            // it will stream this first
            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification> (x=>
                    x.Commit == _commit1 && 
                    x.PreviousCheckpoint==9L,
                TimeSpan.FromMilliseconds(100)
            );
        }

        [Test]
        public void Should_stream_second_commit() {
            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification> (x=>
                x.Commit == _commit2 && // its the second commit
                x.PreviousCheckpoint==_commit1.CheckpointToken, // and it has a pointer to the first commit
                TimeSpan.FromMilliseconds(100)
                );
        }
    }

    public class When_receives_second_commit_which_doesn_not_follow_first : LocalEventStreamActorTests {
        private FakeCommit _commit1;
        private FakeCommit _commit2;
        protected override void Because()
        {
            _commit1 = new FakeCommit { CheckpointToken = 10L };
            _commit2 = new FakeCommit { CheckpointToken = 12L }; //note gap

            sut.Tell(new OrderedCommitNotification(_commit1, 9L), this.TestActor);
            sut.Tell(new CommitNotification(_commit2), this.TestActor);

            // expect this first, but we aren't actually testing for this, in this particular test
            _pollEventStoreProbe.ExpectMsg<EventStorePollerActor.Messages.SendHeadCommit>();
        }

        [Test]
        public void Should_poll_the_event_store() {
            _pollEventStoreProbe.ExpectMsg<EventStorePollerActor.Messages.SendCommitAfterCurrentHeadCheckpointMessage>(
                x=>x.CurrentHeadCheckpoint==_commit1.CheckpointToken,
                TimeSpan.FromMilliseconds(100));
        }
    }

    public class When_receives_commits_out_of_order : LocalEventStreamActorTests {
        private FakeCommit _commit1;
        private FakeCommit _commit2;
        private FakeCommit _commit3;

        protected override void Because() {
            _commit1 = new FakeCommit { CheckpointToken = 10L };
            _commit2 = new FakeCommit { CheckpointToken = 11L };
            _commit3 = new FakeCommit { CheckpointToken = 12L };

            // expect this first, but we aren't actually testing for this, in this particular test
            _pollEventStoreProbe.ExpectMsg<EventStorePollerActor.Messages.SendHeadCommit>();

            sut.Tell(new OrderedCommitNotification(_commit1, 9L), this.TestActor);
            sut.Tell(new CommitNotification(_commit3), this.TestActor); //note sent out of order
            sut.Tell(new CommitNotification(_commit2), this.TestActor);
        }

        [Test]
        public void Should_poll_the_event_store() {
            _pollEventStoreProbe.ExpectMsg<EventStorePollerActor.Messages.SendCommitAfterCurrentHeadCheckpointMessage>(
                x => x.CurrentHeadCheckpoint == _commit1.CheckpointToken,
                TimeSpan.FromMilliseconds(100));
        }

        [Test]
        public void Should_stream_one_two_then_three()  {
            //order of these expectations is important.  it needs to send them in this order
            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification>(x =>
                    x.Commit == _commit1 && // it's the second commit
                    x.PreviousCheckpoint == 9L // and it has a pointer to the first commit
                , TimeSpan.FromMilliseconds(100)
            );

            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification>(x =>
                    x.Commit == _commit2 && // it's the second commit
                    x.PreviousCheckpoint == _commit1.CheckpointToken // and it has a pointer to the first commit
                , TimeSpan.FromMilliseconds(100)
            );

            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification>(x =>
                    x.Commit == _commit3 && // it's the third commit
                    x.PreviousCheckpoint == _commit2.CheckpointToken // and it has a pointer to the second commit
                ,TimeSpan.FromMilliseconds(100)
            );
        }
    }

    public class When_receives_messages_from_poller_which_do_not_follow_last_streamed_checkpoint : LocalEventStreamActorTests {
        private FakeCommit _commit1;
        private FakeCommit _commit2;
        private FakeCommit _commit3;

        protected override void Because()  {
            _commit1 = new FakeCommit { CheckpointToken = 10L };
            _commit2 = new FakeCommit { CheckpointToken = 11L };
            _commit3 = new FakeCommit { CheckpointToken = 12L };

            sut.Tell(new OrderedCommitNotification(_commit1, 9L), this.TestActor);

            sut.Tell(new CommitNotification(_commit1), this.TestActor);
            sut.Tell(new OrderedCommitNotification(_commit3, 11L), this.TestActor);
        }

        [Test]
        public void Should_stream_first_commit()  {
            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification>(x =>
                    x.Commit == _commit1 && // it's the first commit
                    x.PreviousCheckpoint == 9L // and it has a pointer to earlier commit
                , TimeSpan.FromMilliseconds(100)
            );
        }
    }

    public class When_receives_messages_from_poller_which_follow_last_streamed_checkpoint : LocalEventStreamActorTests
    {
        private FakeCommit _commit1;
        private FakeCommit _commit2;
        private FakeCommit _commit3;

        protected override void Because()
        {
            _commit1 = new FakeCommit { CheckpointToken = 10L };
            _commit2 = new FakeCommit { CheckpointToken = 11L };
            _commit3 = new FakeCommit { CheckpointToken = 12L };

            sut.Tell(new CommitNotification(_commit1), this.TestActor);
            sut.Tell(new OrderedCommitNotification(_commit2, _commit1.CheckpointToken), this.TestActor);
        }

        [Test]
        public void Should_stream()  {
            _eventBusStoreProbe.ExpectMsg<OrderedCommitNotification>(x =>
                    x.Commit == _commit2 && // it's the second commit
                    x.PreviousCheckpoint == _commit1.CheckpointToken // and it has a pointer to the first commit
                , TimeSpan.FromMilliseconds(100)
            );
        }
    }
}
