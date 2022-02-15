using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using NUnit.Framework;

namespace EventSaucing.EventStream {
    [TestFixture]
    public abstract class OrderedStreamerTests {
        protected OrderedEventStreamer sut;


        public OrderedStreamerTests() {
            Because();
        }
        protected virtual void Because() { }
    }

    public class When_initialised_with_some_commits : OrderedStreamerTests {
        private OrderedCommitNotification _firstCommit;

        protected override void Because() {
            sut = new OrderedEventStreamer(10L,
                (new[] { 11L, 12L, 13L }).Select(x => new FakeCommit() { CheckpointToken = x }));

            _firstCommit = sut.Next();
        }

        [Test]
        public void Shouldnt_be_finished_as_we_havent_processed_any_yet() {
            sut.IsFinished.Should().BeFalse();
        }

        [Test]
        public void First_commit_should_be_11() {
            _firstCommit.Commit.CheckpointToken.Should().Be(11L);
        }

        [Test]
        public void First_commit_should_follow_10() {
            _firstCommit.PreviousCheckpoint.Should().Be(10L);
        }
    }

    public class When_there_is_a_non_contiguous_checkpooint : OrderedStreamerTests {
        private OrderedCommitNotification _firstCommit;

        protected override void Because() {
            sut = new OrderedEventStreamer(10L,
                (new[] { 12L, 13L }).Select(x => new FakeCommit() { CheckpointToken = x }));

            _firstCommit = sut.Next();
        }

        [Test]
        public void Shouldnt_be_finished_as_we_havent_processed_any_yet() {
            sut.IsFinished.Should().BeFalse();
        }

        [Test]
        public void First_commit_should_be_12() {
            _firstCommit.Commit.CheckpointToken.Should().Be(12L);
        }

        [Test]
        public void First_commit_should_follow_10() {
            _firstCommit.PreviousCheckpoint.Should().Be(10L);
        }
    }

    public class When_exhausted_commit_stream : OrderedStreamerTests {
        private OrderedCommitNotification _last;

        protected override void Because() {
            sut = new OrderedEventStreamer(10L,
                (new[] { 11L, 12L, 13L }).Select(x => new FakeCommit() { CheckpointToken = x }));

            sut.Next();
            sut.Next();
            _last = sut.Next();
        }

        [Test]
        public void Should_be_finished_as_we_have_processed_all() {
            sut.IsFinished.Should().BeTrue();
        }

        [Test]
        public void Last_commit_should_be_13() {
            _last.Commit.CheckpointToken.Should().Be(13L);
        }

        [Test]
        public void Last_commit_should_follow_12() {
            _last.PreviousCheckpoint.Should().Be(12L);
        }
    }
}