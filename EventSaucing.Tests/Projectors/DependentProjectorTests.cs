﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit3;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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

    public class IndependentProjector : ProbingProjector { }

    public class DependentProjector : ProbingProjector { }

    [TestFixture]
    public abstract class DependentProjectorTests : TestKit {
        public DependentProjectorTests() {
            Because();
        }

        protected abstract void Because();
    }

    /// <summary>
    /// Independent + dependent start at 10, and Independent receives 11
    /// </summary>
    public class When_commit_is_sent_to_independent_projector_only : DependentProjectorTests {
        private IActorRef _independentProjector;
        private IActorRef _dependentProjector;
        private TestProbe _probe;
        private IEnumerable<Projector.Messages.AfterProjectorCheckpointStatusChanged> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(Projector.Messages.AfterProjectorCheckpointStatusChanged));

            //both initialised at checkpoint 10
            _independentProjector = Sys.ActorOf<IndependentProjector>(nameof(IndependentProjector));
            _dependentProjector = Sys.ActorOf<DependentProjector>(nameof(DependentProjector));

            //push independent to 11L
            _independentProjector.Tell(new OrderedCommitNotification(new FakeCommit { CheckpointToken = 11L },
                previousCheckpoint: 10L.ToSome()));

            _publishedMessages = _probe.ReceiveN(3)
                .Select(x => (Projector.Messages.AfterProjectorCheckpointStatusChanged)x)
                .ToList();
        }

        [Test]
        public void Independent_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(IndependentProjector));
        }

        [Test]
        public void Dependent_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(DependentProjector));
        }

        [Test]
        public void Independent_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(IndependentProjector));
        }
    }

    /// <summary>
    ///  Independent + dependent start at 10, and Independent receives 11, then dependent receives 11
    /// </summary>
    public class When_commit_is_sent_in_order_to_projectors_with_a_dependency : DependentProjectorTests {
        private IActorRef _independentProjector;
        private IActorRef _dependentProjector;
        private TestProbe _probe;
        private IEnumerable<Projector.Messages.AfterProjectorCheckpointStatusChanged> _publishedMessages;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(Projector.Messages.AfterProjectorCheckpointStatusChanged));

            //both initialised at checkpoint 10
            _independentProjector = Sys.ActorOf<IndependentProjector>(nameof(IndependentProjector));
            _dependentProjector = Sys.ActorOf<DependentProjector>(nameof(DependentProjector));

            //push commit to both in the natural order
            var orderedCommitNotification = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L },
                previousCheckpoint: 10L.ToSome());
            _independentProjector.Tell(orderedCommitNotification);
            _dependentProjector.Tell(orderedCommitNotification);

            _publishedMessages = _probe.ReceiveN(4)
                .Select(x => (Projector.Messages.AfterProjectorCheckpointStatusChanged)x)
                .ToList();
        }

        [Test]
        public void Independent_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(IndependentProjector));
        }

        [Test]
        public void Dependent_should_have_started_at_10() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 10L && x.MyType == typeof(DependentProjector));
        }

        [Test]
        public void Independent_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(IndependentProjector));
        }

        [Test]
        public void Dependent_should_have_advanced_to_11() {
            _publishedMessages
                .Should().ContainSingle(x => x.Checkpoint == 11L && x.MyType == typeof(DependentProjector));
        }
    }

    /// <summary>
    ///  Independent + dependent start at 10, and dependent receives 11
    /// </summary>
    public class When_commit_is_sent_only_to_dependent_projector : DependentProjectorTests {
        private IActorRef _independentProjector;
        private IActorRef _dependentProjector;
        private TestProbe _probe;
        private Projector.Messages.CurrentCheckpoint _dependentCurrentCheckpoint;
        private Projector.Messages.CurrentCheckpoint _independentCurrentCheckpoint;

        protected override void Because() {
            _probe = CreateTestProbe();
            Sys.EventStream.Subscribe(_probe, typeof(Projector.Messages.AfterProjectorCheckpointStatusChanged));

            //both initialised at checkpoint 10
            _independentProjector = Sys.ActorOf<IndependentProjector>(nameof(IndependentProjector));
            _dependentProjector = Sys.ActorOf<DependentProjector>(nameof(DependentProjector));

            //push dependent to 11L, it's now ahead of dependent
            var newCommit = new OrderedCommitNotification(
                new FakeCommit { CheckpointToken = 11L },
                previousCheckpoint: 10L.ToSome());

            //send commit only to the dependent
            _dependentProjector.Tell(newCommit);

            var checkpointDep = _dependentProjector
                .Ask<Projector.Messages.CurrentCheckpoint>(Projector.Messages.SendCurrentCheckpoint.Message);
            var checkpointInd = _independentProjector
                .Ask<Projector.Messages.CurrentCheckpoint>(Projector.Messages.SendCurrentCheckpoint.Message);

            Task.WaitAll(checkpointInd, checkpointDep);
            _dependentCurrentCheckpoint = checkpointDep.Result;
            _independentCurrentCheckpoint = checkpointInd.Result;
        }

        [Test]
        public void Dependent_should_not_have_advanced_to_11_because_it_is_dependent_on_the_other_projector_advancing_first() {
            _dependentCurrentCheckpoint.Checkpoint.Get().Should().Be(10L);
        }

        [Test]
        public void Independent_should_be_on_10_as_it_hasnt_received_11_yet() {
            _independentCurrentCheckpoint.Checkpoint.Get().Should().Be(10L);
        }
    }
}