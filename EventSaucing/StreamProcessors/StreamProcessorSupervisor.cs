using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Routing;
using EventSaucing.EventStream;
using Scalesque;

namespace EventSaucing.StreamProcessors {
    public class StreamProcessorSupervisor : ReceiveActor {
        /// <summary>
        /// Broadcast router which forwards any messages it receives to all Stream Processors it manages
        /// </summary>
        private IActorRef _streamProcessorBroadCastRouter;

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
        }

        protected override void PreStart() {
            base.PreStart();
            //subscribe to ordered event stream and checkpoint changes
            Context.System.EventStream.Subscribe(Self, typeof(OrderedCommitNotification));
            Context.System.EventStream.Subscribe(_streamProcessorBroadCastRouter,
                typeof(StreamProcessor.Messages.AfterStreamProcessorCheckpointStatusSet));
        }

        protected override void PostRestart(Exception reason) {
            // override to avoid duplicate subscription to OrderedCommitNotification
        }

        protected override void PostStop() {
            //unsubscribe to ordered event stream and checkpoint changes
            Context.System.EventStream.Unsubscribe(Self);
            Context.System.EventStream.Unsubscribe(_streamProcessorBroadCastRouter);
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
            _streamProcessorBroadCastRouter.Tell(StreamProcessor.Messages.CatchUp.Message, Self);
        }
    }
}