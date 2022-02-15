using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using EventSaucing.Projectors;
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

        /// <summary>
        /// Holds a pointer to the latest checkpoint we streamed.  None = not projected anything yet
        /// </summary>
        private Option<long> _lastStreamedCheckpoint = Option.None();

        /// <summary>
        /// This tracks the size of runs of commits which we receive from NEventStore, but couldn't project because the cache couldn't serialise them. 
        /// </summary>
        private int _backlogCommitCount = 0;

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="cache">IInMemoryCommitSerialiserCache</param>
        /// <param name="pollerMaker">Func to create <see cref="EventStorePollerActor"/> actor</param>
        public LocalEventStreamActor(IInMemoryCommitSerialiserCache cache, Func<IUntypedActorContext, IActorRef> pollerMaker) {
            _cache = cache;
            _pollerMaker = pollerMaker;

            Receive<CommitNotification>(Received);
            Receive<OrderedCommitNotification>(Received);
        }

        /// <summary>
        /// This message is sent from a node after a commit is created on any node.  Commits can be received out of order.  
        /// </summary>
        /// <param name="msg"></param>
        private void Received(CommitNotification msg)   {
            _cache.Cache(msg.Commit);
            _backlogCommitCount++;

            if (!_lastStreamedCheckpoint.HasValue) {
                // The first commit we receive can't be ordered by this actor because we don't know the Checkpoint number which proceeds it
                // we simply store it and wait for the next commit.  If the next commit follows it, we can send them both out together. 
                // if it doesn't we go to the commit store instead
                _lastStreamedCheckpoint = msg.Commit.CheckpointToken.ToSome(); //this commit is now considered the head
            } else {
                List<OrderedCommitNotification> cachedCommits = _cache.GetCommitsAfter(_lastStreamedCheckpoint.Get());
                if (cachedCommits.Count > 0) {
                    //local cache can produce an ordered stream after the last streamed checkpoint. Stream them out.
                    cachedCommits.ForEach(StreamCommit);
                } else {
                    //local cache can't ensure we have all the commits in order, go to db
                    PollEventStoreWithExponentialBackoff(msg, _lastStreamedCheckpoint.Get());
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
    
        private void PollEventStoreWithExponentialBackoff(CommitNotification msg, long afterCheckpoint) {
            //we poll exponentially, on the size of the backlog of unprojected commits
            if (!IsPowerOfTwo((ulong)_backlogCommitCount))
                return;

            //log that we are going to db.  Situation is entirely normal and expected in distributed cluster
            var currentCheckpoint = _lastStreamedCheckpoint.Map(x => x.ToString()).GetOrElse("no currentcommit");

            Context.GetLogger()
                   .Debug($"Received a commit notification (checkpoint {msg.Commit.CheckpointToken}) whilst currentCheckpoint={currentCheckpoint}.  Commit couldn't be serialised via the cache so polling dbo.Commits with @backlog count={_backlogCommitCount}");

            // this actor stops itself after it has processed the msg
            var eventStorePollerActor = _pollerMaker(Context);

            //ask the poller to get the commits directly from the store
            eventStorePollerActor.Tell(new EventStorePollerActor.Messages.SendCommitAfterCurrentHeadCheckpointMessage(afterCheckpoint,_backlogCommitCount.ToSome()));
        }

        /// <summary>
        /// Message sent by the event store poller
        /// </summary>
        /// <param name="msg"></param>
        private void Received(OrderedCommitNotification msg)  {
            // only send the commit if it follows the last streamed checkpoint, else just ignore it
            if(_lastStreamedCheckpoint.Map(x=> x == msg.PreviousCheckpoint).GetOrElse(false)){
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