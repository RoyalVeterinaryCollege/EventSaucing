using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.Projectors {
    /// <summary>
    ///     An actor which polls the eventstore to create ordered commit notifications
    /// </summary>
    public class EventStorePollerActor : ReceiveActor {
        private readonly IPersistStreams _persistStreams;

        public EventStorePollerActor(IPersistStreams persistStreams) {
            _persistStreams = persistStreams;
            Receive<SendCommitAfterCurrentHeadCheckpointMessage>(msg => Received(msg));
        }

        private void Received(SendCommitAfterCurrentHeadCheckpointMessage msg) {

            Option<long> previousCheckpoint = msg.CurrentHeadCheckpoint;
            var commits = GetCommitsFromPersistentStore(msg);

            foreach (var commit in commits)
            {
                Context.Sender.Tell(new OrderedCommitNotification(commit, previousCheckpoint));
                previousCheckpoint = commit.CheckpointToken.ToSome();
            }

            Context.Stop(Self);
        }

        private IEnumerable<ICommit> GetCommitsFromPersistentStore(SendCommitAfterCurrentHeadCheckpointMessage msg) {
            IEnumerable<ICommit> commits =_persistStreams.GetFrom(msg.CurrentHeadCheckpoint.GetOrElse(() => 0)); //load all commits after checkpoint from db
            if (!msg.NumberOfCommitsToSend.HasValue)
                return commits;

            return commits.Take(msg.NumberOfCommitsToSend.Get());
        }
    }
}