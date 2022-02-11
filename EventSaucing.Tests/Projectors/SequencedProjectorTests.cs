﻿using System;
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
using Scalesque;

namespace EventSaucing.Projectors {
    public class ProbingProjector : Projector {
        protected override void PreStart() {
            //dont call base, else it starts the timer which is confusing when debugging
            SetCheckpoint(10L);
        }

        protected override Task CatchUpAsync() {
            return Task.CompletedTask;
        }

        protected override Task PersistCheckpointAsync() {
            return Task.CompletedTask;
        }

        public override Task ProjectAsync(ICommit commit) {
            SetCheckpoint(commit.CheckpointToken);
            return Task.CompletedTask;
        }
    }

    public class ProceedingProjector : ProbingProjector { }

    public class FollowingProjector : ProbingProjector {
        protected override void PreStart() {
            base.PreStart();
            base.PreceededBy<ProceedingProjector>();
        }
    }

    [TestFixture]
    public abstract class SequencedProjectorTests : TestKit {
        public SequencedProjectorTests() {
            Because();
        }

        protected IActorRef InitialiseProjector<T>() where T : ProbingProjector, new() {
            var projector = Sys.ActorOf<T>(typeof(T).FullName);
            Sys.EventStream.Subscribe(projector, typeof(Projector.Messages.AfterProjectorCheckpointStatusSet));
            return projector;
        }

        protected abstract void Because();
    }

    /// <summary>
    ///     Proceeding + following start at 10, and Proceeding receives 11
    /// </summary>
    public class When_commit_is_sent_to_proceeding_projector_only : SequencedProjectorTests {
        private IActorRef _proceedingProjector;
        private IActorRef _followingProjector;
        private TestProbe _probe;
        private IEnumerable<Projector.Messages.AfterProjectorCheckpointStatusSet> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(Projector.Messages.AfterProjectorCheckpointStatusSet));

            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Projector.Messages.AfterProjectorCheckpointStatusSet
            _followingProjector = InitialiseProjector<FollowingProjector>();
            _proceedingProjector = InitialiseProjector<ProceedingProjector>();

            //push proceeding to 11L
            _proceedingProjector.Tell(new OrderedCommitNotification(new FakeCommit { CheckpointToken = 11L },
                10L.ToSome()));

            _publishedMessages = _probe.ReceiveN(3)
                .Select(x => (Projector.Messages.AfterProjectorCheckpointStatusSet)x)
                .ToList();
        }

        [Test]
        public void Proceeding_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(ProceedingProjector));
        }

        [Test]
        public void Following_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(FollowingProjector));
        }

        [Test]
        public void Proceeding_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(ProceedingProjector));
        }
    }

    /// <summary>
    ///     Proceeding + following start at 10, and Proceeding receives 11, then following receives 11
    /// </summary>
    public class When_commit_is_sent_to_sequenced_projectors_in_order : SequencedProjectorTests {
        private IActorRef _proceedingProjector;
        private IActorRef _followingProjector;
        private TestProbe _probe;
        private IEnumerable<Projector.Messages.AfterProjectorCheckpointStatusSet> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(Projector.Messages.AfterProjectorCheckpointStatusSet));


            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Projector.Messages.AfterProjectorCheckpointStatusSet
            _followingProjector = InitialiseProjector<FollowingProjector>();
            _proceedingProjector = InitialiseProjector<ProceedingProjector>();


            //push commit to both in the natural order
            var orderedCommitNotification = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L },
                10L.ToSome());
            _proceedingProjector.Tell(orderedCommitNotification);
            _followingProjector.Tell(orderedCommitNotification);

            _publishedMessages = _probe.ReceiveN(4)
                .Select(x => (Projector.Messages.AfterProjectorCheckpointStatusSet)x)
                .ToList();
        }

        [Test]
        public void Proceeding_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(ProceedingProjector));
        }

        [Test]
        public void Following_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(FollowingProjector));
        }

        [Test]
        public void Proceeding_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(ProceedingProjector));
        }

        [Test]
        public void Following_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(FollowingProjector));
        }
    }

    /// <summary>
    ///     Proceeding + following start at 10, and following receives 11
    /// </summary>
    public class When_commit_is_sent_only_to_following_projector : SequencedProjectorTests {
        private IActorRef _proceedingProjector;
        private IActorRef _followingProjector;
        private Projector.Messages.CurrentCheckpoint _followingCurrentCheckpoint;
        private Projector.Messages.CurrentCheckpoint _proceedingCurrentCheckpoint;

        protected override void Because() {
            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Projector.Messages.AfterProjectorCheckpointStatusSet
            _followingProjector = InitialiseProjector<FollowingProjector>();
            _proceedingProjector = InitialiseProjector<ProceedingProjector>();


            var newCommit = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L },
                10L.ToSome());

            //push following to 11L, it's now ahead of Proceeding
            //send commit only to following
            _followingProjector.Tell(newCommit);

            var followerCheckpoint = _followingProjector
                .Ask<Projector.Messages.CurrentCheckpoint>(Projector.Messages.SendCurrentCheckpoint.Message);
            var proceedingCheckPoint = _proceedingProjector
                .Ask<Projector.Messages.CurrentCheckpoint>(Projector.Messages.SendCurrentCheckpoint.Message);

            Task.WaitAll(proceedingCheckPoint, followerCheckpoint);
            _followingCurrentCheckpoint = followerCheckpoint.Result;
            _proceedingCurrentCheckpoint = proceedingCheckPoint.Result;
        }

        [Test]
        public void Proceeding_should_be_on_10_as_it_hasnt_received_11_yet() {
            _proceedingCurrentCheckpoint.Checkpoint.Get().Should().Be(10L);
        }

        [Test]
        public void
            Following_should_not_have_advanced_to_11_because_proceeding_is_still_on_10() {
            _followingCurrentCheckpoint.Checkpoint.Get().Should().Be(10L);
        }
    }

    /// <summary>
    ///     Proceeding + following start at 10. Following receives 11, then Proceeding receives 11
    /// </summary>
    public class When_commit_is_sent_to_sequenced_projectors_out_of_order : SequencedProjectorTests {
        private IActorRef _proceedingProjector;
        private IActorRef _followingProjector;
        private Projector.Messages.CurrentCheckpoint _followingCurrentCheckpoint;
        private Projector.Messages.CurrentCheckpoint _proceedingCurrentCheckpoint;

        protected override void Because() {
            //both initialised at checkpoint 10
            //the order of creation matters here. We need to create follower first because otherwise it wont receive proceeder's Projector.Messages.AfterProjectorCheckpointStatusSet
            _followingProjector = InitialiseProjector<FollowingProjector>();
            _proceedingProjector = InitialiseProjector<ProceedingProjector>();


            var newCommit = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L },
                10L.ToSome());

            //push following, then Proceeding, in that order
            _followingProjector.Tell(newCommit);
            _proceedingProjector.Tell(newCommit);

            var checkpointDep = _followingProjector
                .Ask<Projector.Messages.CurrentCheckpoint>(Projector.Messages.SendCurrentCheckpoint.Message);
            var checkpointInd = _proceedingProjector
                .Ask<Projector.Messages.CurrentCheckpoint>(Projector.Messages.SendCurrentCheckpoint.Message);

            Task.WaitAll(checkpointInd, checkpointDep);
            _followingCurrentCheckpoint = checkpointDep.Result;
            _proceedingCurrentCheckpoint = checkpointInd.Result;
        }

        [Test]
        public void Proceeding_should_be_on_11() {
            _proceedingCurrentCheckpoint.Checkpoint.Get().Should().Be(11L);
        }

        [Test]
        public void Following_should_have_advanced_to_11_because_proceeding_has_also_received_11() {
            _followingCurrentCheckpoint.Checkpoint.Get().Should().Be(11L);
        }
    }
}