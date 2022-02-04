using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Routing;
using EventSaucing.EventStream;
using Scalesque;

namespace EventSaucing.Projectors {
    public class ProjectorSupervisor : ReceiveActor  {
        /// <summary>
        /// Broadcast router which forwards any messages it receives to all Projectors
        /// </summary>
        private IActorRef _projectorsBroadCastRouter;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="projectorMaker">Func which returns all the projectors to be supervised</param>
        public ProjectorSupervisor(Func<IUntypedActorContext, IEnumerable<IActorRef>> projectorMaker) {
            InitialiseProjectors(projectorMaker);

            Receive<OrderedCommitNotification>(msg => _projectorsBroadCastRouter.Tell(msg, Self));
        }

        protected override void PreStart() {
            base.PreStart();
            Context.System.EventStream.Subscribe(Self, typeof(OrderedCommitNotification));
        }

        private void InitialiseProjectors(Func<IUntypedActorContext, IEnumerable<IActorRef>> projectorMaker) {
            IEnumerable<IActorRef> projectors = projectorMaker(Context);

            //put the projectors in a broadcast router
            _projectorsBroadCastRouter = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(projectors.Map(_ => _.Path.ToString()))), "ProjectionBroadcastRouter");

            //tell them to catchup, else they will sit and wait for user activity 
            _projectorsBroadCastRouter.Tell(CatchUpMessage.Message, Self);
        }
    }
}

