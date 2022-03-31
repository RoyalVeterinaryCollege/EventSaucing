using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit3;
using EventSaucing.EventStream;
using EventSaucing.StreamProcessors;
using EventSaucing.StreamProcessors.Projectors;
using NUnit.Framework;
using Scalesque;

namespace EventSaucing.Projectors
{

    [TestFixture]
    public abstract class ProjectorSupervisorTests : TestKit {
        /// <summary>
        /// <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        protected IActorRef sut;

        protected TestProbe _projector1;
        protected TestProbe _projector2;


        public ProjectorSupervisorTests()  {
            _projector1 = CreateTestProbe();
            _projector2 = CreateTestProbe();

            //inject dependencies
            Func<IUntypedActorContext, IEnumerable<IActorRef>> maker = (ctx) => new []{_projector1, _projector2};
            sut = Sys.ActorOf(Props.Create<StreamProcessorSupervisor>(maker));

            Because();
        }

        protected virtual void Because() { }
    }

    public class When_ProjectorSupervisor_starts : ProjectorSupervisorTests {
        [Test]
        public void Should_send_catchup() {
            _projector1.ExpectMsg<StreamProcessor.Messages.CatchUp>(TimeSpan.FromMilliseconds(100));
            _projector2.ExpectMsg<StreamProcessor.Messages.CatchUp>(TimeSpan.FromMilliseconds(100));
        }
    }

    public class When_ordered_commit_published_on_event_bus_ProjectorSupervisor : ProjectorSupervisorTests  {
        private OrderedCommitNotification _msg;

        protected override void Because() {
            var commit = new FakeCommit() { CheckpointToken = 99L };
            _msg = new OrderedCommitNotification(commit, 98L);

            sut.Tell(_msg);

            //ignore these ones for this test
            _projector1.ExpectMsg<StreamProcessor.Messages.CatchUp>();//TimeSpan.FromMilliseconds(100));
            _projector2.ExpectMsg<StreamProcessor.Messages.CatchUp>(); //TimeSpan.FromMilliseconds(100));
        }

        [Test]
        public void Should_send_commit_to_projectors() {
            _projector1.ExpectMsg<OrderedCommitNotification>(x=> x == _msg,TimeSpan.FromMilliseconds(100));
            _projector2.ExpectMsg<OrderedCommitNotification>(x=> x == _msg, TimeSpan.FromMilliseconds(100));
        }
    }
}
