using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit3;
using EventSaucing.EventStream;
using EventSaucing.StreamProcessors;
using FluentAssertions;
using NEventStore;
using NUnit.Framework;
using Scalesque;

namespace EventSaucing.Projectors {

    /// <summary>
    /// A projector whose projection method can be injected at run time
    /// </summary>
    public class ErrorThrowingStreamProcessor : StreamProcessor {
        private Func<ICommit, Task<bool>> _projectionMethod;

        public ErrorThrowingStreamProcessor() : base(new FakePersistStreams()) {
            //allow caller to alter projection method implementation
            Receive<Func<ICommit, Task<bool>>>(msg => _projectionMethod = msg);
        }

        protected override void PreStart() {
            InitialCheckpoint = 10L.ToSome();
            base.PreStart();
        }

        protected override void StartTimer() {
            //Don't start timer
        }

        protected override Task CatchUpAsync() {
            return Task.CompletedTask;
        }

        protected override Task PersistCheckpointAsync() {
            return Task.CompletedTask;
        }

        public override Task<bool> ProcessAsync(ICommit commit) {
            return _projectionMethod(commit);
        }
    }

    [TestFixture]
    public abstract class ProjectorErrorHandlingTests : TestKit {
        protected IActorRef _sut;

        public ProjectorErrorHandlingTests() {
            Because();
        }

        protected virtual void Because() {
            _sut = Sys.ActorOf<ErrorThrowingStreamProcessor>(typeof(ErrorThrowingStreamProcessor).FullName);
        }
    }

    public class When_projector_doesnt_throw_error_during_projection : ProjectorErrorHandlingTests {
        private TestProbe _probe;
        private List<StreamProcessor.Messages.AfterStreamProcessorCheckpointStatusSet> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(StreamProcessor.Messages.AfterStreamProcessorCheckpointStatusSet));

            base.Because();


            // impl which doesn't error
            Func<ICommit, Task<bool>> parp = msg => Task.FromResult(true);
            _sut.Tell(parp);

            //send the commit
            _sut.Tell(new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L }, 10L));

            _publishedMessages = _probe.ReceiveN(2)
                .Select(x => (StreamProcessor.Messages.AfterStreamProcessorCheckpointStatusSet)x)
                .ToList();
        }

        [Test]
        public void Should_start_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(ErrorThrowingStreamProcessor));
        }

        [Test]
        public void Should_advance_checkpoint_to_11_as_commit_received() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(ErrorThrowingStreamProcessor));
        }
    }


    // note the behaviour between When_projector_does_throw_error_during_projection and When_projector_doesnt_throw_error_during_projection is mostly the same.
    // This is because the only difference between a projector which throws, and one which doesnt is the logging of the error
    public class When_projector_does_throw_error_during_projection : ProjectorErrorHandlingTests {
        private TestProbe _probe;
        private List<StreamProcessor.Messages.AfterStreamProcessorCheckpointStatusSet> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(StreamProcessor.Messages.AfterStreamProcessorCheckpointStatusSet));

            base.Because();


            // impl which does error
            Func<ICommit, Task<bool>> parp = msg => throw new NotImplementedException();
            _sut.Tell(parp);

            //send the commit
            _sut.Tell(new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L }, 10L));

            _publishedMessages = _probe.ReceiveN(2, TimeSpan.FromDays(1))
                .Select(x => (StreamProcessor.Messages.AfterStreamProcessorCheckpointStatusSet)x)
                .ToList();
        }

        [Test]
        public void Should_start_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(ErrorThrowingStreamProcessor));
        }

        [Test]
        public void Should_advance_checkpoint_to_11_as_commit_received_and_error_thrown_should_be_handled() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(ErrorThrowingStreamProcessor));
        }
    }
}