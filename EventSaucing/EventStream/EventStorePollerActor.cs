using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.EventStream {
    /// <summary>
    ///     An actor which polls the eventstore to create ordered commit notifications
    /// </summary>
    public class EventStorePollerActor : ReceiveActor {

        public static class Messages {
            /// <summary>
            /// Message sent to ask for a commit notification to be ordered
            /// </summary>
            public class SendCommitAfterCurrentHeadCheckpointMessage  {

                public Option<long> CurrentHeadCheckpoint { get; }
                public Option<int> NumberOfCommitsToSend { get; }

                [DebuggerStepThrough]
                public SendCommitAfterCurrentHeadCheckpointMessage(Option<long> currentHeadCheckpoint, Option<int> numberOfCommitsToSend)
                {
                    CurrentHeadCheckpoint = currentHeadCheckpoint;
                    NumberOfCommitsToSend = numberOfCommitsToSend;
                }
            }
        }
        private readonly IPersistStreams _persistStreams;

        public EventStorePollerActor(IPersistStreams persistStreams) {
            _persistStreams = persistStreams;
            Receive<Messages.SendCommitAfterCurrentHeadCheckpointMessage>(Received);
        }

        private void Received(Messages.SendCommitAfterCurrentHeadCheckpointMessage msg) {
            Option<long> previousCheckpoint = msg.CurrentHeadCheckpoint;
            var commits = GetCommitsFromPersistentStore(msg);

            foreach (var commit in commits)
            {
                Context.Sender.Tell(new OrderedCommitNotification(commit, previousCheckpoint));
                previousCheckpoint = commit.CheckpointToken.ToSome();
            }

            Context.Stop(Self);
        }

        private IEnumerable<ICommit> GetCommitsFromPersistentStore(Messages.SendCommitAfterCurrentHeadCheckpointMessage msg) {
            IEnumerable<ICommit> commits =_persistStreams.GetFrom(msg.CurrentHeadCheckpoint.GetOrElse(() => 0)); //load all commits after checkpoint from db
            if (!msg.NumberOfCommitsToSend.HasValue)
                return commits;

            return commits.Take(msg.NumberOfCommitsToSend.Get());
        }
    }
}