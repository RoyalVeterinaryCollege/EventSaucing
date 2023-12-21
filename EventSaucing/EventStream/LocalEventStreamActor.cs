using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using EventSaucing.HostedServices;
using Microsoft.Extensions.Logging;
using Scalesque;

namespace EventSaucing.EventStream {
    /// <summary>
    /// Actor which converts a distributed unordered stream of CommitNotification messages into a local stream of ordered OrderedCommitNotification messages 
    /// </summary>
    public class LocalEventStreamActor : ReceiveActor {
        /// <summary>
        /// The pub/sub topic where commit notifications are published to
        /// </summary>
        public static string PubSubCommitNotificationTopic = "/eventsaucing/coreservices/commitnotification/";

        /// <summary>
        /// local in-mem cache of recent commits
        /// </summary>
        private readonly IInMemoryCommitSerialiserCache _cache;

        /// <summary>
        /// Function which creates <see cref="EventStorePollerActor"/>
        /// </summary>
        private readonly Func<IUntypedActorContext, IActorRef> _pollerMaker;

        private readonly ILogger<LocalEventStreamActor> _logger;

        /// <summary>
        /// Holds a pointer to the latest checkpoint we streamed.  None = not projected anything yet
        /// </summary>
        private Option<long> _lastStreamedCheckpoint = Option.None();

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="cache">IInMemoryCommitSerialiserCache</param>
        /// <param name="pollerMaker">Func to create <see cref="EventStorePollerActor"/> actor</param>
        /// <param name="logger"></param>
        public LocalEventStreamActor(IInMemoryCommitSerialiserCache cache, Func<IUntypedActorContext, IActorRef> pollerMaker, ILogger<LocalEventStreamActor> logger) {
            _cache = cache;
            _pollerMaker = pollerMaker;
            _logger = logger;

            Receive<CommitNotification>(Received);
            Receive<OrderedCommitNotification>(Received);
            Receive<Stop>(stop => Context.Stop(Self));
        }

        protected override void PreStart() {
            base.PreStart();
            _logger.LogInformation("Starting");
            InitialiseFromHeadCommit();
        }

        /// <summary>
        /// Initialises the actor from the head commit in dbo.Commits
        /// </summary>
        private void InitialiseFromHeadCommit() {
            // this actor stops itself after it has processed the msg
            var eventStorePollerActor = _pollerMaker(Context);
            eventStorePollerActor.Tell(new EventStorePollerActor.Messages.SendHeadCommit());
        }

        /// <summary>
        /// This message is sent from a node after a commit is created on any node.  Commits can be received out of order.  
        /// </summary>
        /// <param name="msg"></param>
        private void Received(CommitNotification msg)   {
            _cache.Cache(msg.Commit);

            if (!_lastStreamedCheckpoint.HasValue) {
                // we haven't initialised yet, so we can't order this commit.  We need to go to the db to find the head commit
                InitialiseFromHeadCommit();
            } else {
                List<OrderedCommitNotification> cachedCommits = _cache.GetCommitsAfter(_lastStreamedCheckpoint.Get());
                if (cachedCommits.Count > 0) {
                    //local cache can produce an ordered stream after the last streamed checkpoint. Stream them out.
                    cachedCommits.ForEach(StreamCommit);
                } else {
                    //local cache can't ensure we have all the commits in order, go to db

                    //log that we are going to db.  Situation is entirely normal and expected in distributed cluster
                    var currentCheckpoint = _lastStreamedCheckpoint.Map(x => x.ToString()).GetOrElse("no current commit");

                    Context.GetLogger()
                        .Debug($"Received a commit notification (checkpoint {msg.Commit.CheckpointToken}) whilst currentCheckpoint={currentCheckpoint}.  Commit couldn't be ordered via the cache so polling dbo.Commits");

                    PollEventStore(_lastStreamedCheckpoint.Get());
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

        private void PollEventStore(long afterCheckpoint) {
            _logger.LogDebug($"Polling event store after checkpoint {afterCheckpoint}");
            // this actor stops itself after it has processed the msg
            var eventStorePollerActor = _pollerMaker(Context);

            //ask the poller to get the commits directly from the store
            eventStorePollerActor.Tell(
                new EventStorePollerActor.Messages.SendCommitAfterCurrentHeadCheckpointMessage(afterCheckpoint)
                );
        }

        /// <summary>
        /// Message sent by the event store poller
        /// </summary>
        /// <param name="msg"></param>
        private void Received(OrderedCommitNotification msg)  {
            // if we haven't sent a message yet, send this one, else only send the commit if it follows the last streamed checkpoint, else just ignore it
            if (_lastStreamedCheckpoint.IsEmpty) {
                _logger.LogInformation($"Streamed first commit {msg.Commit.CheckpointToken}");
                _cache.Cache(msg.Commit);
                StreamCommit(msg);
            } else if(_lastStreamedCheckpoint.Map(x=> x == msg.PreviousCheckpoint).GetOrElse(false)){
                StreamCommit(msg);
            }
        }

        /// <summary>
        /// Streams the OrderedCommitNotification via the local EventBus
        /// </summary>
        /// <param name="msg"></param>
        private void StreamCommit(OrderedCommitNotification msg) {
            _backlogCommitCount = 0; //reset the backlog counter
            _lastStreamedCheckpoint = msg.Commit.CheckpointToken.ToSome(); //update head pointer
            Context.System.EventStream.Publish(msg);
        }
    }
}