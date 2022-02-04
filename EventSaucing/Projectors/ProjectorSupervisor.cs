using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.DI.Core;
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
            /*
            //Reflect on assembly to identify projectors and have DI create them
            var projectorsMetaData =
                (from type in projectorTypeProvider.GetProjectorTypes()
                 select new { Type = type, ActorRef = Context.ActorOf(Context.DI().Props(type), type.FullName), ProjectorId = type.GetProjectorId() }
                ).ToList();
            *?
             */
            IEnumerable<IActorRef> projectors = projectorMaker(Context);

            //put the projectors in a broadcast router
            _projectorsBroadCastRouter = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(projectors.Map(_ => _.Path.ToString()))), "ProjectionBroadcastRouter");

            //tell them to catchup, else they will sit and wait for user activity 
            _projectorsBroadCastRouter.Tell(new CatchUpMessage(), Self);
        }
    }
}

