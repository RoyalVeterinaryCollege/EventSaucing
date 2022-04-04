using System;
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

    /// <summary>
    /// A SP whose Process method can be injected at run time
    /// </summary>
    public class ErrorThrowingStreamProcessor : StreamProcessor {
        private Func<ICommit, Task<bool>> _processMethod;

        public ErrorThrowingStreamProcessor() : base(new FakePersistStreams(), new FakeCheckpointPersister()) {
            //allow caller to alter process method implementation
            Receive<Func<ICommit, Task<bool>>>(msg => _processMethod = msg);
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
            return _processMethod(commit);
        }
    }

    [TestFixture]
    public abstract class ProcessorErrorHandlingTests : TestKit {
        protected IActorRef _sut;

        public ProcessorErrorHandlingTests() {
            Because();
        }

        protected virtual void Because() {
            _sut = Sys.ActorOf<ErrorThrowingStreamProcessor>(typeof(ErrorThrowingStreamProcessor).FullName);
        }
    }

    public class When_processor_doesnt_throw_error_during_processing : ProcessorErrorHandlingTests {
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


    // note the behaviour between When_processor_does_throw_error_during_procession and When_processor_doesnt_throw_error_during_processing is mostly the same.
    // This is because the only difference between a processor which throws, and one which doesnt is the logging of the error
    public class WhenProcessorDoesThrowErrorDuringProcessing : ProcessorErrorHandlingTests {
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