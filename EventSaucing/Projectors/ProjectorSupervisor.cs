using System;
using System.Linq;
using Akka.Actor;
using Akka.DI.Core;
using Akka.Routing;
using EventSaucing.EventStream;
using Scalesque;

namespace EventSaucing.Projectors
{

    public class ProjectorSupervisor : ReceiveActor
    {
        //todo ProjectorSupervisor totally unfinished

        private ActorPath _projectorsBroadCastRouter;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="projectorTypeProvider">IProjectorTypeProvider a contract which returns Projectors to be wired up</param>
        public ProjectorSupervisor(IProjectorTypeProvider projectorTypeProvider)
        {
            InitialiseProjectors(projectorTypeProvider);

        }

        private void InitialiseProjectors(IProjectorTypeProvider projectorTypeProvider)
        {
            //Reflect on assembly to identify projectors and have DI create them
            var projectorsMetaData =
                (from type in projectorTypeProvider.GetProjectorTypes()
                 select new { Type = type, ActorRef = Context.ActorOf(Context.DI().Props(type), type.FullName), ProjectorId = type.GetProjectorId() }
                ).ToList();

            //put the projectors in a broadcast router
            _projectorsBroadCastRouter = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(projectorsMetaData.Map(_ => _.ActorRef.Path.ToString()))), "ProjectionBroadcastRouter").Path;

            //tell them to catchup, else they will sit and wait for the first user activity (from the first commit)
            Context.ActorSelection(_projectorsBroadCastRouter).Tell(new CatchUpMessage());
        }


        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we don't create extra instances each time
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason) { }
        

        /// <summary>
        /// Sends the commit to the projectors for projection
        /// </summary>
        /// <param name="msg"></param>
        private void SendCommitToProjectors(OrderedCommitNotification msg)
        {
            Context.ActorSelection(_projectorsBroadCastRouter).Tell(msg); //pass message on for projection
        }
    }
}

