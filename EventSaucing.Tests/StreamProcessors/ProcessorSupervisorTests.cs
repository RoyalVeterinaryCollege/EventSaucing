using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit;
using EventSaucing.EventStream;
using NUnit.Framework;

namespace EventSaucing.StreamProcessors
{

    [TestFixture]
    public abstract class ProcessorSupervisorTests : TestKit {
        /// <summary>
        /// <see cref="StreamProcessorSupervisor"/>
        /// </summary>
        protected IActorRef sut;

        protected TestProbe _processor1;
        protected TestProbe _processor2;


        public ProcessorSupervisorTests()  {
            _processor1 = CreateTestProbe();
            _processor2 = CreateTestProbe();

            //inject dependencies
            Func<IUntypedActorContext, IEnumerable<IActorRef>> maker = (ctx) => new []{_processor1, _processor2};
            sut = Sys.ActorOf(Props.Create<StreamProcessorSupervisor>(maker));

            Because();
        }

        protected virtual void Because() { }
    }

    public class WhenOrderedCommitPublishedOnEventBusProcessorSupervisor : ProcessorSupervisorTests  {
        private OrderedCommitNotification _msg;

        protected override void Because() {
            var commit = new FakeCommit() { CheckpointToken = 99L };
            _msg = new OrderedCommitNotification(commit, 98L);

            sut.Tell(_msg);

            //ignore these ones for this test
            //_processor1.ExpectMsg<StreamProcessor.Messages.CatchUp>();//TimeSpan.FromMilliseconds(100));
            //_processor2.ExpectMsg<StreamProcessor.Messages.CatchUp>(); //TimeSpan.FromMilliseconds(100));
        }

        [Test]
        public void Should_send_commit_to_projectors() {
            _processor1.ExpectMsg<OrderedCommitNotification>(x=> x == _msg,TimeSpan.FromMilliseconds(100));
            _processor2.ExpectMsg<OrderedCommitNotification>(x=> x == _msg, TimeSpan.FromMilliseconds(100));
        }
    }
}
