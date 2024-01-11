using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using EventSaucing.Akka.Messages;
using EventSaucing.NEventStore;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.Akka.Actors {
    /// <summary>
    /// An actor which polls the eventstore to create ordered commit notifications
    /// </summary>
    public class EventStorePollerActor : ReceiveActor {
        private readonly IPersistStreams _persistStreams;

        public EventStorePollerActor(IPersistStreams persistStreams) {
            _persistStreams = persistStreams;
            Receive<SendCommitAfterCurrentHeadCheckpointMessage>(msg => Received(msg));
        }

        private void Received(SendCommitAfterCurrentHeadCheckpointMessage msg) {
            var mbPreviousCheckpoint = msg.CurrentHeadCheckpoint.ToSome();
            var commits = GetCommitsFromPersistentStore(msg);

            foreach (var commit in commits)
            {
                Context.Sender.Tell(new OrderedCommitNotification(commit, mbPreviousCheckpoint));
                mbPreviousCheckpoint = commit.CheckpointToken.ToSome();
            }

            Context.Stop(Self);
        }

        private IEnumerable<ICommit> GetCommitsFromPersistentStore(SendCommitAfterCurrentHeadCheckpointMessage msg) {
            IEnumerable<ICommit> commits =_persistStreams.GetFrom(msg.CurrentHeadCheckpoint); //load all commits after checkpoint from db
            return commits;
        }
    }
}