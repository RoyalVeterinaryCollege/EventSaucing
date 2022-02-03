using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.DI.Core;
using Akka.Event;
using Akka.Routing;
using EventSaucing.Projectors;
using Scalesque;

namespace EventSaucing.EventStream {
    /// <summary>
    /// Actor which converts a distributed unordered stream of CommitNotification messages into a local stream of ordered OrderedCommitNotification messages 
    /// </summary>
    public class LocalEventStreamActor : ReceiveActor {
        //todo remove all the projector code and add publication of OrderedCommitNotifcation to local eventbus
        
        /// <summary>
        /// The pub/sub topic where commit notifications are published to
        /// </summary>
        public static string PubSubCommitNotificationTopic = "/eventsaucing/coreservices/commitnotification/";
        private readonly IInMemoryCommitSerialiserCache _cache;

        /// <summary>
        /// Holds a pointer to the latest checkpoint that we have projected.  None = not projected anything yet
        /// </summary>
        private Option<long> _currentCheckpoint = Option.None();

        /// <summary>
        /// A helper for determining which is the next commit we need
        /// </summary>
        readonly CommitOrderer _orderer  = new CommitOrderer();

        /// <summary>
        /// This tracks the size of runs of commits which we receive from NEventStore, but couldn't project because the cache couldn't serialise them. 
        /// </summary>
        private int _backlogCommitCount = 0;

        private ActorPath _projectorsBroadCastRouter;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="projectorTypeProvider">IProjectorTypeProvider a contract which returns Projectors to be wired up</param>
        public LocalEventStreamActor(IInMemoryCommitSerialiserCache cache, IProjectorTypeProvider projectorTypeProvider) {
            _cache = cache;
            InitialiseProjectors(projectorTypeProvider);

            Receive<CommitNotification>(Received);
            Receive<OrderedCommitNotification>(Received);
        }

        private void InitialiseProjectors(IProjectorTypeProvider projectorTypeProvider)  {
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

        protected override void PreStart() {
            base.PreStart();
            //subscribe to distributed commit notification messages
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Subscribe(PubSubCommitNotificationTopic, Self));
        }

        /// <summary>
        /// Overriding postRestart to disable the call to preStart() after restarts.  This means children are restarted, and we don't create extra instances each time
        /// </summary>
        /// <param name="reason"></param>
        protected override void PostRestart(Exception reason) { }

        /// <summary>
        /// This message is sent from a node after a commit is created.  Commits can be received out of order.  
        /// </summary>
        /// <param name="msg"></param>
        private void Received(CommitNotification msg)   {
            _cache.Cache(msg.Commit);
            _backlogCommitCount++;

            if (!_currentCheckpoint.HasValue) HandleFirstCommitAfterStartup(msg);  
            else {
                List<OrderedCommitNotification> cachedCommits = _cache.GetCommitsAfter(_currentCheckpoint.Get());
                if (cachedCommits.Count > 0) {
                    //local cache can ensure we have all the commits in order: project them
                    cachedCommits.ForEach(SendCommitToProjectors);
                    _backlogCommitCount = 0;
                }
                else {
                    //local cache can't ensure we have all the commits in order
                    PollEventStoreWithExponentialBackoff(msg, _currentCheckpoint);
                }
            }
        }

        /// <summary>
        /// Is the number a power of 2?
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        /// <remarks>https://stackoverflow.com/questions/600293/how-to-check-if-a-number-is-a-power-of-2</remarks>
        bool IsPowerOfTwo(ulong x) => (x & (x - 1)) == 0;
    
        private void PollEventStoreWithExponentialBackoff(CommitNotification msg, Option<long> afterCheckpoint) {
            //we poll exponentially, on the size of the backlog of unprojected commits
            if (!IsPowerOfTwo((ulong)_backlogCommitCount))
                return;

            //log @warning.  Detected situation is entirely normal and expected, but should be rare.  
            var currentCheckpoint = _currentCheckpoint.Map(x => x.ToString()).GetOrElse("no currentcommit");

            Context.GetLogger()
                   .Warning(
                       "Received a commit notification (checkpoint {0}) whilst currentcheckpoint={1}.  Commit couldn't be serialised via the cache so polling with @backlog count={2}",
                       msg.Commit.CheckpointToken, currentCheckpoint, _backlogCommitCount);

            // this actor stops itself after it has processed the msg
            var eventStorePollerActor = MakeNewEventStorePollerActor();

            //ask the poller to get the commits directly from the store
            Context.ActorSelection(eventStorePollerActor.Path)
                   .Tell(new SendCommitAfterCurrentHeadCheckpointMessage(afterCheckpoint,_backlogCommitCount.ToSome()));
        }

        /// <summary>
        /// Message sent by the event store poller
        /// </summary>
        /// <param name="msg"></param>
        private void Received(OrderedCommitNotification msg)  {
            //guard against old messages being streamed from the poller, we can safely ignore them
            if (_orderer.IsNextCheckpoint(_currentCheckpoint, msg)) {
                SendCommitToProjectors(msg);
            }
        }

        /// <summary>
        /// The first commit we recieve can't be ordered by this actor.  
        /// </summary>
        /// <param name="msg"></param>
        private void HandleFirstCommitAfterStartup(CommitNotification msg) {
            //NEventStore doesn't allow the getting of a commit prior to a checkpoint. so we cant' create the ordered commit notification, instead we just treat this commit as the head
            _currentCheckpoint = msg.Commit.CheckpointToken.ToSome(); //this commit is now considered the head

            //but tell the projectors to catchup, otherwise 1st commit is not projected
            Context.ActorSelection(_projectorsBroadCastRouter).Tell(new CatchUpMessage()); //tell projectors to catch up
        }

        private static IActorRef MakeNewEventStorePollerActor() {
            var eventStorePollerActor =
                Context.ActorOf(
                    Context.DI()
                           .Props<EventStorePollerActor>()
                           .WithSupervisorStrategy(global::Akka.Actor.SupervisorStrategy.StoppingStrategy));
            return eventStorePollerActor;
        }

        public class LastLocalCheckpoint {
            public long MaxCheckpointNumber { get; set; }
        }

        /// <summary>
        /// Sends the commit to the projectors for projection
        /// </summary>
        /// <param name="msg"></param>
        private void SendCommitToProjectors(OrderedCommitNotification msg) {
            _backlogCommitCount = 0; //reset the backlog counter
            _currentCheckpoint = msg.Commit.CheckpointToken.ToSome(); //update head pointer
            Context.ActorSelection(_projectorsBroadCastRouter).Tell(msg); //pass message on for projection
        }      
    }
}