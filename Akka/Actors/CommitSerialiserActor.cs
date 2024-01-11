using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.DI.Core;
using Akka.Event;
using Akka.Routing;
using EventSaucing.Akka.Messages;
using EventSaucing.NEventStore;
using EventSaucing.Projector;
using EventSaucing.Storage;
using Scalesque;

namespace EventSaucing.Akka.Actors {

    /// <summary>
    /// Actor which receives commits from the event store.  Its only job is to send commits in the correct order to the local projection supervisor
    /// </summary>
    public class CommitSerialiserActor : ReceiveActor {
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

        public CommitSerialiserActor(IDbService dbService, IInMemoryCommitSerialiserCache cache) {
            _cache = cache;
            InitialiseProjectors();

            Receive<CommitNotification>(msg => { Received(msg); });
            Receive<OrderedCommitNotification>(msg => { Received(msg); });
        }

        /// <summary>
        /// This message is sent from NEventstore after a commit is created.  Commits might not be sent in the correct order however...
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
                    PollEventStore(_currentCheckpoint.Get());
                }
            }
        }
    
        private void PollEventStore(long checkpoint) {
            Context.GetLogger().Debug("Polling event store after checkpoint {CheckPoint}", checkpoint);
            // this actor stops itself after it has processed the msg
            var eventStorePollerActor = MakeNewEventStorePollerActor();

            //ask the poller to get the commits directly from the store
            eventStorePollerActor.Tell(new SendCommitAfterCurrentHeadCheckpointMessage(checkpoint));
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
        /// The first commit we receive can't be ordered by this actor.  
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

        private void InitialiseProjectors() {
            //Reflect on assembly to identify projectors and have DI create them
            var projectorTypes = ProjectorHelper.FindAllProjectorsInProject();
            var projectorsMetaData =
                (from type in projectorTypes
                 select new { Type = type, ActorRef = Context.ActorOf(Context.DI().Props(type), type.FullName), ProjectorId = type.GetProjectorId() }
               ).ToList();

            //put the projectors in a broadcast router
            _projectorsBroadCastRouter = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(projectorsMetaData.Map(_ => _.ActorRef.Path.ToString()))), "ProjectionBroadcastRouter").Path;

            //tell them to catchup, else they will sit and wait for the first user activity (from the first commit)
            Context.ActorSelection(_projectorsBroadCastRouter).Tell(new CatchUpMessage()); 
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