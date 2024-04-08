using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Routing;
using EventSaucing.EventStream;
using Scalesque;

namespace EventSaucing.StreamProcessors {
    public class StreamProcessorSupervisor : ReceiveActor {
        public class Messages {
            /// <summary>
            /// When this message is received on the event stream, the supervisor will publish <cref name="SendStatusesResponse"/> to all stream processors
            /// </summary>
            public class SendStatuses;
            public class SendStatusesResponse {
                public Dictionary<string, StreamProcessor.Messages.InternalState[]> Statuses { get; }

                public SendStatusesResponse(Dictionary<string, StreamProcessor.Messages.InternalState[]> statuses) {
                    Statuses = statuses;
                }
            }
        }
        /// <summary>
        /// Broadcast router which forwards any messages it receives to all Stream Processors it manages
        /// </summary>
        private IActorRef _streamProcessorBroadCastRouter;

        /// <summary>
        /// Number of status messages to keep per stream processor
        /// </summary>
        public const int NumberOfStatusMessagesToKeep = 30;

        /// <summary>
        /// A cache of status messages for stream processors
        /// </summary>
        Dictionary<string, StatusMessageCache> _statusMessageCache = new ();

        /// <summary>
        /// Instantiates
        /// </summary>
        /// <param name="streamProcessorFactory">Func which returns all the projectors to be supervised</param>
        public StreamProcessorSupervisor(Func<IUntypedActorContext, IEnumerable<IActorRef>> streamProcessorFactory) {
            InitialiseStreamProcessors(streamProcessorFactory);

            Receive<OrderedCommitNotification>(msg => _streamProcessorBroadCastRouter.Tell(msg, Self));
            ReceiveAsync<Stop>(async stop => {
                Context.System.EventStream.Unsubscribe(Self, typeof(OrderedCommitNotification));

                // _streamProcessorBroadCastRouter.Tell(new Broadcast(new Stop()));
                var shutdown = await _streamProcessorBroadCastRouter.GracefulStop(TimeSpan.FromSeconds(5), new Stop());
                return;
            });
            Receive<StreamProcessor.Messages.InternalState>(Received);
            Receive<Messages.SendStatuses>(Received);
        }

        private void Received(Messages.SendStatuses msg) {
             Sender.Tell(new Messages.SendStatusesResponse(_statusMessageCache.ToDictionary(_ => _.Key, _ => _.Value.Statuses)));
        }

        /// <summary>
        /// stash the last n status messages per stream processor, in case we are asked for them
        /// </summary>
        /// <param name="msg"></param>
        private void Received(StreamProcessor.Messages.InternalState msg) {
            if (!_statusMessageCache.ContainsKey(msg.Name)){
                _statusMessageCache[msg.Name] = new StatusMessageCache(NumberOfStatusMessagesToKeep);
            }

            _statusMessageCache[msg.Name].AddStatus(msg);
        }

        protected override void PreStart() {
            base.PreStart();
            Context.System.EventStream.Subscribe(Self, typeof(OrderedCommitNotification));
            Context.System.EventStream.Subscribe(Self, typeof(StreamProcessor.Messages.InternalState));
        }

        protected override void PostRestart(Exception reason) {
            // override to avoid duplicate subscription to OrderedCommitNotification
        }

        protected override void PostStop() {
            //unsubscribe to ordered event stream and checkpoint changes
            Context.System.EventStream.Unsubscribe(Self);
        }

        /// <summary>
        /// Creates all the projectors as supervised children
        /// </summary>
        /// <param name="streamProcessorMaker"></param>
        private void InitialiseStreamProcessors(Func<IUntypedActorContext, IEnumerable<IActorRef>> streamProcessorMaker) {
            IEnumerable<IActorRef> streamProcessors = streamProcessorMaker(Context);

            //put the SP in a broadcast router
            _streamProcessorBroadCastRouter =
                Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(streamProcessors.Map(_ => _.Path.ToString()))),
                    "StreamProcessorBroadcastRouter");
            
            //tell them to catchup, else they will sit and wait for user activity 
            //_streamProcessorBroadCastRouter.Tell(StreamProcessor.Messages.CatchUp.Message, Self);
            // this has moved to PreStart of the StreamProcessor.  This means SP will catch up after crashing as well.
        }
    }
}