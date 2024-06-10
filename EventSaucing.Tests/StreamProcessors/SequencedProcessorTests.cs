using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit3;
using EventSaucing.EventStream;
using FluentAssertions;
using NEventStore;
using NUnit.Framework;

namespace EventSaucing.StreamProcessors {
    public class ProbingStreamProcessor : StreamProcessor {

        public ProbingStreamProcessor() : base(new FakePersistStreams(), new FakeCheckpointPersister())
        {
            
        }

        protected override void StartTimers() {
           //Don't start timer
        }

        protected override Task CatchUpStartAsync() {
            return Task.CompletedTask;
        }

        protected override Task PersistCheckpointAsync() {
            return Task.CompletedTask;
        }

        public override Task<bool> ProcessAsync(ICommit commit) {
            return Task.FromResult(true);
        }
    }

    public class ProceedingStreamProcessor : ProbingStreamProcessor { }

    public class FollowingStreamProcessor : ProbingStreamProcessor {
        protected override void PreStart() {
            base.PreStart();
            base.ProceededBy<ProceedingStreamProcessor>();
        }
    }

    [TestFixture]
    public abstract class SequencedProcessorTests : TestKit {
        public SequencedProcessorTests() {
            Because();
        }

        protected IActorRef InitialiseProcessor<T>() where T : ProbingStreamProcessor, new() {
            var processor = Sys.ActorOf<T>(typeof(T).FullName);
            Sys.EventStream.Subscribe(processor, typeof(StreamProcessor.Messages.CurrentCheckpoint));
            return processor;
        }

        protected abstract void Because();
    }

    /// <summary>
    ///     Proceeding + following start at 10, and Proceeding receives 11
    /// </summary>
    public class WhenCommitIsSentToProceedingProcessorOnly : SequencedProcessorTests {
        private IActorRef _proceedingProcessor;
        private IActorRef _followingProcessor;
        private TestProbe _probe;
        private IEnumerable<StreamProcessor.Messages.CurrentCheckpoint> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(StreamProcessor.Messages.CurrentCheckpoint));

            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Processor.Messages.AfterProcessorCheckpointStatusSet
            _followingProcessor = InitialiseProcessor<FollowingStreamProcessor>();
            _proceedingProcessor = InitialiseProcessor<ProceedingStreamProcessor>();

            //push proceeding to 11L
            _proceedingProcessor.Tell(new OrderedCommitNotification(new FakeCommit { CheckpointToken = 11L },10L));

            _publishedMessages = _probe.ReceiveN(3)
                .Select(x => (StreamProcessor.Messages.CurrentCheckpoint)x)
                .ToList();
        }

        [Test]
        public void Proceeding_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(ProceedingStreamProcessor));
        }

        [Test]
        public void Following_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(FollowingStreamProcessor));
        }

        [Test]
        public void Proceeding_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(ProceedingStreamProcessor));
        }
    }

    /// <summary>
    ///     Proceeding + following start at 10, and Proceeding receives 11, then following receives 11
    /// </summary>
    public class WhenCommitIsSentToSequencedProcessorsInOrder : SequencedProcessorTests {
        private IActorRef _proceedingProcessor;
        private IActorRef _followingProcessor;
        private TestProbe _probe;
        private IEnumerable<StreamProcessor.Messages.CurrentCheckpoint> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(StreamProcessor.Messages.CurrentCheckpoint));


            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Processor.Messages.AfterProcessorCheckpointStatusSet
            _followingProcessor = InitialiseProcessor<FollowingStreamProcessor>();
            _proceedingProcessor = InitialiseProcessor<ProceedingStreamProcessor>();


            //push commit to both in the natural order
            var orderedCommitNotification = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L }, 10L);
            _proceedingProcessor.Tell(orderedCommitNotification);
            _followingProcessor.Tell(orderedCommitNotification);

            _publishedMessages = _probe.ReceiveN(4)
                .Select(x => (StreamProcessor.Messages.CurrentCheckpoint)x)
                .ToList();
        }

        [Test]
        public void Proceeding_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(ProceedingStreamProcessor));
        }

        [Test]
        public void Following_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(FollowingStreamProcessor));
        }

        [Test]
        public void Proceeding_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(ProceedingStreamProcessor));
        }

        [Test]
        public void Following_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(FollowingStreamProcessor));
        }
    }

    /// <summary>
    ///     Proceeding + following start at 10, and following receives 11
    /// </summary>
    public class WhenCommitIsSentOnlyToFollowingProcessor : SequencedProcessorTests {
        private IActorRef _proceedingProcessor;
        private IActorRef _followingProcessor;
        private StreamProcessor.Messages.CurrentCheckpoint _followingCurrentCheckpoint;
        private StreamProcessor.Messages.CurrentCheckpoint _proceedingCurrentCheckpoint;

        protected override void Because() {
            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Processor.Messages.AfterProcessorCheckpointStatusSet
            _followingProcessor = InitialiseProcessor<FollowingStreamProcessor>();
            _proceedingProcessor = InitialiseProcessor<ProceedingStreamProcessor>();


            var newCommit = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L }, 10L);

            //push following to 11L, it's now ahead of Proceeding
            //send commit only to following
            _followingProcessor.Tell(newCommit);

            var followerCheckpoint = _followingProcessor
                .Ask<StreamProcessor.Messages.CurrentCheckpoint>(new StreamProcessor.Messages.PublishCheckpoint());
            var proceedingCheckPoint = _proceedingProcessor
                .Ask<StreamProcessor.Messages.CurrentCheckpoint>(new StreamProcessor.Messages.PublishCheckpoint());

            Task.WaitAll(proceedingCheckPoint, followerCheckpoint);
            _followingCurrentCheckpoint = followerCheckpoint.Result;
            _proceedingCurrentCheckpoint = proceedingCheckPoint.Result;
        }

        [Test]
        public void Proceeding_should_be_on_10_as_it_hasnt_received_11_yet() {
            _proceedingCurrentCheckpoint.Checkpoint.Should().Be(10L);
        }

        [Test]
        public void Following_should_not_have_advanced_to_11_because_proceeding_is_still_on_10() {
            _followingCurrentCheckpoint.Checkpoint.Should().Be(10L);
        }
    }

    /// <summary>
    ///     Proceeding + following start at 10. Following receives 11, then Proceeding receives 11
    /// </summary>
    public class WhenCommitIsSentToSequencedProcessorsOutOfOrder : SequencedProcessorTests {
        private IActorRef _proceedingProcessor;
        private IActorRef _followingProcessor;
        private StreamProcessor.Messages.CurrentCheckpoint _followingCurrentCheckpoint;
        private StreamProcessor.Messages.CurrentCheckpoint _proceedingCurrentCheckpoint;

        protected override void Because() {
            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Processor.Messages.AfterProcessorCheckpointStatusSet
            _followingProcessor = InitialiseProcessor<FollowingStreamProcessor>();
            _proceedingProcessor = InitialiseProcessor<ProceedingStreamProcessor>();

            var newCommit = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L }, 10L);

            // send to Following, then Proceeding, in that order
            _followingProcessor.Tell(newCommit);
            _proceedingProcessor.Tell(newCommit);

            // give follower time to process all the messages
            Task.Delay(1000).Wait();

            var checkpointDep = _followingProcessor
                .Ask<StreamProcessor.Messages.CurrentCheckpoint>(new StreamProcessor.Messages.PublishCheckpoint());
            var checkpointInd = _proceedingProcessor
                .Ask<StreamProcessor.Messages.CurrentCheckpoint>(new StreamProcessor.Messages.PublishCheckpoint());

            Task.WaitAll(checkpointInd, checkpointDep);
            _followingCurrentCheckpoint = checkpointDep.Result;
            _proceedingCurrentCheckpoint = checkpointInd.Result;
        }

        [Test]
        public void Proceeding_should_be_on_11() {
            _proceedingCurrentCheckpoint.Checkpoint.Should().Be(11L);
        }

        [Test]
        public void Following_should_have_advanced_to_11_because_proceeding_has_also_received_11() {
            _followingCurrentCheckpoint.Checkpoint.Should().Be(11L,because: "proceeding_has_also_received_11");
        }
    }
}