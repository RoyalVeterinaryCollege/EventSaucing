using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DistributedData;
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

        protected Dictionary<Type, long> MessageCounts { get; } = new();

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
            /// Message sent by StreamProcessor to report its internal state
            /// </summary>
            /// <param name="checkpoint"></param>
            /// <param name="name"></param>
            /// <param name="akkaAddress"></param>
            /// <param name="messageCounts"></param>
            /// <param name="proceedingStreamProcessors"></param>
            /// <param name="isCatchingUp"></param>
            /// <param name="message"></param>
            public class InternalState(
                string name,
                DateTime messageCreated,
                long checkpoint,
                string akkaAddress,
                Dictionary<string, long> messageCounts,
                Dictionary<string, long> proceedingStreamProcessors,
                bool isCatchingUp,
                string message) {
                public string Name { get; } = name;
                public DateTime MessageCreated { get;  } = messageCreated;
                public long Checkpoint { get; } = checkpoint;
                public string AkkaAddress { get; } = akkaAddress;
                public Dictionary<string, long> MessageCounts { get; } = messageCounts;
                public Dictionary<string, long> ProceedingStreamProcessors { get; } = proceedingStreamProcessors;
                public bool IsCatchingUp { get; } = isCatchingUp;
                public string Message { get; } = message;
            }

            /// <summary>
            ///     Tell StreamProcessor to catch up by going to commit store to stream commits
            /// </summary>
            public class CatchUp ;

            /// <summary>
            ///     Tell StreamProcessor to persist its checkpoint state to db
            /// </summary>
            public class PersistCheckpoint;

            /// <summary>
            /// Message published on EventStream after the StreamProcessor's checkpoint changes
            /// </summary>
            public class CurrentCheckpoint(Type myType, long checkpoint) {
                public Type MyType { get; } = myType;
                public long Checkpoint { get; } = checkpoint;
                public override string ToString() => $"{MyType.Name} @ {Checkpoint}";
            }

            /// <summary>
            /// When a StreamProcessor receives this, it will publish its <see cref="CurrentCheckpoint"/> to the event stream.  It's used in catch-up to reduce event stream spam.
            /// </summary>
            public class PublishCheckpoint(bool reply = false) {
                /// <summary>
                /// If true, the StreamProcessor will reply to the sender with the current checkpoint as well as publishing it to the event stream
                /// </summary>
                public bool Reply { get; } = reply;
            }

            /// <summary>
            /// When a StreamProcessor receives this, it will publish <see cref="InternalState"/> message to the event stream
            /// </summary>
            public class PublishInternalState;
        }

        /// <summary>
        /// Our proceeding StreamProcessors.  StreamProcessor type -> last known checkpoint for that StreamProcessor
        /// </summary>
        public Dictionary<Type, long> ProceedingStreamProcessors { get; } = new ();

        public StreamProcessor(IPersistStreams persistStreams, IStreamProcessorCheckpointPersister checkpointPersister) {
            _persistStreams = persistStreams;
            _checkpointPersister = checkpointPersister;

            ReceiveAsync<Messages.CatchUp>(ReceivedAsync);
            ReceiveAsync<OrderedCommitNotification>(ReceivedAsync);
            ReceiveAsync<Messages.PersistCheckpoint>(ReceivedAsync);
            Receive<Messages.PublishInternalState>(msg => {
                AddMessageCount(msg);
                Context.System.EventStream.Publish(GetInternalStateMessage());
            });
            ReceiveAsync<Messages.CurrentCheckpoint>(ReceivedAsync);
            Receive<Messages.PublishCheckpoint>(msg => {
                AddMessageCount(msg);
                var currentCheckpoint = new Messages.CurrentCheckpoint(GetType(), Checkpoint);
                Context.System.EventStream.Publish(currentCheckpoint);
                if(msg.Reply) Sender.Tell(currentCheckpoint); // also tell sender the current checkpoint (makes testing easier)
            });
        }

        private async Task ReceivedAsync(Messages.PersistCheckpoint msg) {
	        AddMessageCount(msg);
	        await PersistCheckpointAsync();
        }

        /// <summary>
        /// Gets a message indicating the internal state of the StreamProcessor
        /// </summary>
        /// <returns></returns>
        protected virtual Messages.InternalState GetInternalStateMessage() => new (
            name:GetType().Name, 
            messageCreated: DateTime.Now,
            checkpoint:Checkpoint, 
            akkaAddress:Self.Path.ToString(),
            // make a copy of the dictionaries so state not shared 
            messageCounts:MessageCounts.Select(kv=>(kv.Key.Name,kv.Value)).ToDictionary(kv=>kv.Name,kv=>kv.Value),
            proceedingStreamProcessors:ProceedingStreamProcessors.Select(kv=>(kv.Key.Name,kv.Value)).ToDictionary(kv=>kv.Name,kv=>kv.Value),
            isCatchingUp:_isCatchingUp,
            message:""
            );

        /// <summary>
        /// Keeps a count of messages received by type
        /// </summary>
        /// <param name="msg"></param>
        private void AddMessageCount(object msg) {
            // increment message count
            var count = MessageCounts.ContainsKey(msg.GetType()) ? MessageCounts[msg.GetType()] + 1 : 1;

            // handle overflow by resetting to 1
            if (count == long.MaxValue) count = 1;

            MessageCounts[msg.GetType()] = count;
        }

        private async Task ReceivedAsync(Messages.CurrentCheckpoint msg) {
            AddMessageCount(msg);
            if (ProceedingStreamProcessors.ContainsKey(msg.MyType)) {
                // guard against receiving same checkpoint multiple times (SP send out their status every 5 seconds, not just when it changes)
                if (ProceedingStreamProcessors[msg.MyType] == msg.Checkpoint) return;

                ProceedingStreamProcessors[msg.MyType] = msg.Checkpoint;

                // if we are in catch up mode, we may be able to process the next commit now
                if (_isCatchingUp) {
                    await CatchUpTryAdvanceAsync();
                } else if (AllProceedingStreamProcessorsAhead()) {
                    // we are not in catch up mode, but we have fallen behind, catch up
                    string proceedingStreamProcessorNames = string.Join(",", ProceedingStreamProcessors.Select(kv => $"{kv.Key.Name}:{kv.Value}"));
                    string catchupReason =$"StreamProcessor going into catchup mode @ {Checkpoint} as ProceedingStreamProcessors have advanced beyond our checkpoint: {proceedingStreamProcessorNames}";
					await CatchUpStartAsync(catchupReason);
                }
            }
        }

        /// <summary>
        /// Set initial state of actor on start up
        /// </summary>
        protected override void PreStart() {
            SetCheckpoint(_checkpointPersister.GetInitialCheckpointAsync(this).Result);
            PersistCheckpointAsync().Wait(); // this ensures a persisted checkpoint on first instantiation
            StartTimers();

            // tell self to catch-up, else it will sit and wait for user activity
            Self.Tell(new Messages.CatchUp());
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
        protected virtual async Task PersistCheckpointAsync() => await _checkpointPersister.PersistCheckpointAsync(this, Checkpoint);

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
            if (!ProceedingStreamProcessors.Any()) return true;

            return ProceedingStreamProcessors
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
        protected void ProceededBy<T>() where T : StreamProcessor {
            if(!ProceedingStreamProcessors.Any()) {
                // if this is the first registration, subscribe to these messages, we don't do this by default as there can be a lot of them
                Context.System.EventStream.Subscribe(Self, typeof(Messages.CurrentCheckpoint));
            }

            var type = typeof(T);
            if (!ProceedingStreamProcessors.ContainsKey(type)) ProceedingStreamProcessors[type] = 0L;
        }

        /// <summary>
        /// Sets the StreamProcessor's checkpoint and publishes the changed event to the event stream
        /// </summary>
        /// <param name="checkpoint"></param>
        protected void SetCheckpoint(long checkpoint) {
            Checkpoint = checkpoint;

            // only publish if we are not catching up. We don't want to spam the event stream with checkpoint updates during catch-up
            if (!_isCatchingUp) {
                Context.System.EventStream.Publish(new Messages.CurrentCheckpoint(GetType(), Checkpoint));
            }
        }

        /// <summary>
        /// Holds the timer which periodically tells StreamProcessor to persist its checkpoint
        /// </summary>
        public ITimerScheduler Timers { get; set; }

        private async Task ReceivedAsync(Messages.CatchUp arg) {
            AddMessageCount(arg);
            
            if (!_isCatchingUp) await CatchUpStartAsync($"Received CatchUp message from {Context.Sender.Path}");
        }

        /// <summary>
        /// Starts timer to periodically persist checkpoint to db
        /// </summary>
        protected virtual void StartTimers() {
            // every 5 seconds, persist our checkpoint to db
            Timers.StartPeriodicTimer("persist_checkpoint",
                new Messages.PersistCheckpoint(),
                // random start up delay so SPs don't all hit DB at once
                TimeSpan.FromMilliseconds(Rnd.Value.Next(2000, 10000)), 
                TimeSpan.FromSeconds(5));

            // every 10 seconds, publish our internal status to the event stream
            Timers.StartPeriodicTimer("publish_status",
                new Messages.PublishInternalState(),
                TimeSpan.FromSeconds(10)
            );

            // every 5 seconds publish our checkpoint to the event stream (required by any dependent StreamProcessors)
            Timers.StartPeriodicTimer("publish_checkpoint",
                new Messages.PublishCheckpoint(reply:false),
                TimeSpan.FromMilliseconds(200)
            );
        }

     
        /// <summary>
        /// Starts the catch-up process where commits are streamed from the commit store.  
        /// </summary>
        /// <returns></returns>
        protected virtual async Task CatchUpStartAsync(string catchUpReason) {
            _isCatchingUp = true;

            //load all commits after our current checkpoint from db
            var startingCheckpoint = Checkpoint;
            Context.GetLogger()
                .Info($"Catchup started from checkpoint {Checkpoint} because {catchUpReason}");

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

            // publish our checkpoint to the event stream
            Self.Tell(new Messages.PublishCheckpoint(reply:false));;
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
                    , string.Join(",",ProceedingStreamProcessors.Select(kv=> $"{kv.Key}:{kv.Value}"))
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
        }

        /// <summary>
        /// Method called when catch up has finished
        /// </summary>
        /// <returns></returns>
        protected virtual Task OnCatchupFinishedAsync() {
            return Task.CompletedTask;
        }

        protected virtual async Task ReceivedAsync(OrderedCommitNotification msg) {
            AddMessageCount(msg);

            // guard going ahead of a proceeding StreamProcessor
            if (!AllProceedingStreamProcessorsAhead()) {
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
                await CatchUpStartAsync($"Our {Checkpoint} checkpoint has fallen behind OrderedCommitNotification stream {msg.Commit.CheckpointToken} checkpoint");
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