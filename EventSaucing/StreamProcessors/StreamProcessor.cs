using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using EventSaucing.EventStream;
using EventSaucing.NEventStore;
using NEventStore;
using NEventStore.Persistence;
using Failure = Akka.Actor.Status.Failure;

namespace EventSaucing.StreamProcessors {
    public abstract class StreamProcessor : ReceiveActor, IWithTimers {
        private readonly IPersistStreams _persistStreams;
        private readonly IStreamProcessorCheckpointPersister _checkpointPersister;

        /// <summary>
        /// Bool. if true, the StreamProcessor is in catch up mode and will stream commits to itself from <see cref="OrderedEventStreamer"/>
        /// </summary>
        private bool _isCatchingUp;

        /// <summary>
        /// Used during Catchup to stream events from the commit store
        /// </summary>
        private OrderedEventStreamer _catchupCommitStream;
      
        /// <summary>
        /// All message types sent to and from StreamProcessors
        /// </summary>
        public static class Messages {
            /// <summary>
            ///     Tell StreamProcessor to catch up by going to commit store to stream commits
            /// </summary>
            public class CatchUp {
                static CatchUp() {
                    Message = new CatchUp();
                }

                private CatchUp() { }

                public static CatchUp Message { get; }
            }

            /// <summary>
            ///     Tell StreamProcessor to persist its checkpoint state to db
            /// </summary>
            public class PersistCheckpoint {
                static PersistCheckpoint() {
                    Message = new PersistCheckpoint();
                }

                private PersistCheckpoint() { }

                public static PersistCheckpoint Message { get; }
            }

            /// <summary>
            /// Asks StreamProcessor to send its current checkpoint. Replies with <see cref="CurrentCheckpoint"/>
            /// </summary>
            public class SendCurrentCheckpoint {
                static SendCurrentCheckpoint() {
                    Message = new SendCurrentCheckpoint();
                }

                private SendCurrentCheckpoint() { }
                public static SendCurrentCheckpoint Message { get; }
            }

            /// <summary>
            /// A reply to the <see cref="CurrentCheckpoint"/> message
            /// </summary>
            public class CurrentCheckpoint {
                public long Checkpoint { get; }

                public CurrentCheckpoint(long checkpoint) {
                    Checkpoint = checkpoint;
                }
            }

            public class DependUponStreamProcessors {
                /// <summary>
                /// The Type of the SP that depends on the SP listed
                /// </summary>
                public Type MyType { get; }

                /// <summary>
                /// Reference to the StreamProcessor
                /// </summary>
                public IActorRef MyRef { get; }

                /// <summary>
                /// A list of Types of StreamProcessors upon which this StreamProcessor depends. If list is empty, this StreamProcessor depends on no other StreamProcessors.
                /// </summary>
                public IReadOnlyList<Type> StreamProcessors { get; }

                public DependUponStreamProcessors(Type myType, IActorRef myRef, IReadOnlyList<Type> streamProcessors) {
                    MyType = myType;
                    MyRef = myRef;
                    StreamProcessors = streamProcessors;
                }
            }

            /// <summary>
            /// Message published on EventStream after the StreamProcessor's checkpoint changes
            /// </summary>
            public class AfterStreamProcessorCheckpointStatusSet {
                public Type MyType { get; }
                public long Checkpoint { get; }

                public AfterStreamProcessorCheckpointStatusSet(Type myType, long checkpoint) {
                    MyType = myType;
                    Checkpoint = checkpoint;
                }
            }
        }

        private const string TimerName = "persist_checkpoint";

        /// <summary>
        /// Our proceeding StreamProcessors.  StreamProcessor type -> last known checkpoint for that StreamProcessor
        /// </summary>
        public Dictionary<Type, long> PreceedingStreamProcessors { get; } = new Dictionary<Type, long>();

        public StreamProcessor(IPersistStreams persistStreams, IStreamProcessorCheckpointPersister checkpointPersister) {
            _persistStreams = persistStreams;
            _checkpointPersister = checkpointPersister;

            ReceiveAsync<Messages.CatchUp>(ReceivedAsync);
            ReceiveAsync<OrderedCommitNotification>(ReceivedAsync);
            ReceiveAsync<Messages.PersistCheckpoint>(msg => PersistCheckpointAsync());
            Receive<Messages.SendCurrentCheckpoint>(msg => {
                try {
                    Sender.Tell(new Messages.CurrentCheckpoint(Checkpoint), Self);
                }
                catch (Exception e) {
                    Sender.Tell(new Failure (e), Self);
                }
            });
            ReceiveAsync<Messages.AfterStreamProcessorCheckpointStatusSet>(ReceivedAsync);
        }

        private async Task ReceivedAsync(Messages.AfterStreamProcessorCheckpointStatusSet msg) {
            if (PreceedingStreamProcessors.ContainsKey(msg.MyType)) {
                PreceedingStreamProcessors[msg.MyType] = msg.Checkpoint;

                // if we are in catch up mode, we may be able to process the next commit now
                if (_isCatchingUp) {
                    await CatchUpTryAdvanceAsync();
                }
            }
        }

        /// <summary>
        /// Set initial state of actor on start up
        /// </summary>
        protected override void PreStart() {
            SetCheckpoint(_checkpointPersister.GetInitialCheckpointAsync(this).Result);
            PersistCheckpointAsync().Wait(); // this ensures a persisted checkpoint on first instantiation
            StartTimer();
        }

        protected override void PostStop() {
            base.PostStop();
            // unsubscribe from subscriptions
            Context.System.EventStream.Unsubscribe(Self);
        }

        /// <summary>
        /// Persist checkpoint to db
        /// </summary>
        /// <returns></returns>
        protected virtual Task PersistCheckpointAsync() => _checkpointPersister.PersistCheckpointAsync(this, Checkpoint);

        /// <summary>
        /// Processes the commit.  
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>Bool True if checkpoint should be persisted</returns>
        public abstract Task<bool> ProcessAsync(ICommit commit);

        /// <summary>
        /// Checks if all proceeding StreamProcessors are ahead of us
        /// </summary>
        /// <returns>bool True if we have no proceeding StreamProcessors or all proceeding StreamProcessors have a higher checkpoint than us</returns>
        protected bool AllProceedingStreamProcessorsAhead() {
            if (!PreceedingStreamProcessors.Any()) return true;

            return PreceedingStreamProcessors
                .Values
                .All(proceedingCheckpoint => proceedingCheckpoint > Checkpoint);
        }

        /// <summary>
        /// Gets sets the current checkpoint of the StreamProcessor.  Don't set property directly, call <see cref="SetCheckpoint"/>
        /// </summary>
        public long Checkpoint { get; private set; }

        /// <summary>
        /// Turns this StreamProcessor into a sequenced StreamProcessor. This StreamProcessor's Checkpoint will never be greater than the proceeding StreamProcessor.
        ///
        /// This means it's safe for this StreamProcessor to access the other's read models
        /// </summary>
        /// <typeparam name="T"></typeparam>
        protected void PreceededBy<T>() where T : StreamProcessor {
            // if this is the first registration, subscribe to the local event stream as well
            if(!PreceedingStreamProcessors.Any()) {
                Context.System.EventStream.Subscribe(Self, typeof(Messages.AfterStreamProcessorCheckpointStatusSet));
            }

            var type = typeof(T);
            if (!PreceedingStreamProcessors.ContainsKey(type)) PreceedingStreamProcessors[type] = 0L;
        }

        /// <summary>
        /// Sets the StreamProcessor's checkpoint and publishes the changed event to the event stream
        /// </summary>
        /// <param name="checkpoint"></param>
        protected void SetCheckpoint(long checkpoint) {
            Checkpoint = checkpoint;
            Context.System.EventStream.Publish(new Messages.AfterStreamProcessorCheckpointStatusSet(GetType(), checkpoint));
        }

        /// <summary>
        /// Holds the timer which periodically tells StreamProcessor to persist its checkpoint
        /// </summary>
        public ITimerScheduler Timers { get; set; }

        private async Task ReceivedAsync(Messages.CatchUp arg) {
            if (!_isCatchingUp) await CatchUpStartAsync();
        }

        /// <summary>
        /// Starts timer to periodically persist checkpoint to db
        /// </summary>
        protected virtual void StartTimer() {
            // every 5 seconds, persist our checkpoint to db
            Timers.StartPeriodicTimer(TimerName,
                Messages.PersistCheckpoint.Message,
                // random start up delay so SPs don't all hit DB at once
                TimeSpan.FromMilliseconds(Rnd.Value.Next(2000, 10000)), 
                TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Starts the catch-up process where commits are streamed from the commit store.  
        /// </summary>
        /// <returns></returns>
        protected virtual async Task CatchUpStartAsync() {
            _isCatchingUp = true;

            //load all commits after our current checkpoint from db
            var startingCheckpoint = Checkpoint;
            Context.GetLogger()
                .Info($"Catchup started from checkpoint {startingCheckpoint}");

            _catchupCommitStream = new OrderedEventStreamer(startingCheckpoint, _persistStreams);
            await CatchUpTryAdvanceAsync();
        }
        /// <summary>
        /// Finishes the catch-up process.  
        /// </summary>
        /// <returns></returns>
        private async Task CatchUpFinishAsync() {
            _isCatchingUp = false;
            _catchupCommitStream = null;
            await PersistCheckpointAsync();
            await OnCatchupFinishedAsync();
            Context.GetLogger().Info($"Catchup finished at {Checkpoint}");
        }

        /// <summary>
        /// If it can, it will try to advance Checkpoint by sending the next catch-up commit in the stream to Self.
        /// </summary>
        /// <returns></returns>
        private async Task CatchUpTryAdvanceAsync() {
            // guard finished
            if (_catchupCommitStream.IsFinished) {
                await CatchUpFinishAsync();
                return;
            }

            // we only should only advance if all proceeding StreamProcessors are ahead of us
            if (!AllProceedingStreamProcessorsAhead()) {
                Context
                    .GetLogger()
                    .Debug("StreamProcessor is in catch-up mode @ {Checkpoint} but is waiting for proceeding StreamProcessors {ProceedingStreamProcessors} to advance before processing the next commit"
                    , Checkpoint
                    , string.Join(",",PreceedingStreamProcessors.Select(kv=> $"{kv.Key}:{kv.Value}"))
                    );
                return;
            }

            // guard to ensure we can actually process the next commit
            var nextCommit = _catchupCommitStream.Peek().Get(); //get safe as we checked we aren't finished above

            if (nextCommit.PreviousCheckpoint != Checkpoint) {
                Context
                    .GetLogger()
                    .Debug("StreamProcessor is in catch-up mode @ {Checkpoint} but cant advance because its current Checkpoint is not the previous checkpoint {PreviousCheckpoint} of the next commit in the catch-up stream" 
                    , Checkpoint, nextCommit.PreviousCheckpoint);
                return;
            }

            // safe to advance, send the commit to ourselves
            // commit is sent, so we can interleave commits, and <see cref="Messages.AfterStreamProcessorCheckpointStatusSet"/> messages from any proceeding StreamProcessors.
            var msg = _catchupCommitStream.Next();
            Context.Self.Tell(msg);
            //await ReceivedAsync(msg);
        }

        /// <summary>
        /// Method called when catch up has finished
        /// </summary>
        /// <returns></returns>
        protected virtual Task OnCatchupFinishedAsync() {
            return Task.CompletedTask;
        }

        protected virtual async Task ReceivedAsync(OrderedCommitNotification msg) {
            // guard going ahead of a proceeding StreamProcessor
            if (!AllProceedingStreamProcessorsAhead()) {
                if (Checkpoint == msg.PreviousCheckpoint) {
                    // we want to project this commit, but we can't because proceeding StreamProcessors haven't processed it yet

                    // schedule this commit to be resent to us in the future
                    Timers.StartSingleTimer(key: $"commitid:{msg.Commit.CommitId}",
                        msg,
                        TimeSpan.FromMilliseconds(100));
                }

                return;
            }

            // guard against receiving a commit we have already processed
            if (Checkpoint > msg.PreviousCheckpoint) {
                // we have already processed this commit
                Context
                    .GetLogger()
                    .Debug("Received a commit notification for a checkpoint {Checkpoint} which is behind our checkpoint {Checkpoint}",
                    msg.Commit.CheckpointToken, Checkpoint);
                    return;
            }

            // if commit's previous checkpoint matches our current, process it
            if (Checkpoint == msg.PreviousCheckpoint) {
                await Advance(msg);
            } else {
                // this commit is too far ahead to process it. We have fallen behind, catch up

                // if we are in catch-up mode, safe to drop this message
                if (_isCatchingUp) {
                    // we are already in catch up mode and this msg was likely sent by LocalEventStreamActor
                    // we will eventually see this commit at the right time via Catchup mode, so safe to ignore this message
                    Context
                        .GetLogger()
                        .Debug($"Received a commit notification for a checkpoint which is in our future, but dropped it as we were in catch-up mode (ICommit checkpoint {msg.Commit.CheckpointToken}) ahead of our checkpoint ({Checkpoint}). This ICommit was likely sent by LocalEventStreamActor and doesn't represent a failure.");
                    return;
                }

                // go into catch up mode
                await CatchUpStartAsync();
            }

            // If we are in catch up mode, stream the next commit to Self
            if (_isCatchingUp) {
                await CatchUpTryAdvanceAsync();
            }
        }

        /// <summary>
        /// Advances the StreamProcessor to the next commit by processing it
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private async Task Advance(OrderedCommitNotification msg) {
            try {
                bool shouldPersistCheckpoint = await ProcessAsync(msg.Commit);

                // advance to next checkpoint
                SetCheckpoint(msg.Commit.CheckpointToken);

                // save the checkpoint, if we processed it
                if (shouldPersistCheckpoint) await PersistCheckpointAsync();
            } catch (Exception e) {
                Context.GetLogger().Error(e,$"Exception caught when StreamProcessor {GetType().FullName} tried to process checkpoint {msg.Commit.CheckpointToken} for aggregate {msg.Commit.AggregateId()}");
                // save checkpoint on error, so status table reflects state of StreamProcessor
                await PersistCheckpointAsync();
                throw;
            }
        }

        /// <summary>
        /// Shared random number factory, used for randomly timed persistence of current checkpoint. Wrapped in Lazy for thread-safe initialisation.
        ///
        /// Sharing the Random means that there is no chance that each SP happens to get the same seed as they all initialise at the same point during startup
        /// </summary>
        protected static readonly Lazy<Random> Rnd = new Lazy<Random>(() => new Random());
    }
}