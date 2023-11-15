using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Dapper;
using EventSaucing.Storage;
using NEventStore;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.EventStream {
    /// <summary>
    ///     An actor which polls the eventstore to create ordered commit notifications
    /// </summary>
    public class EventStorePollerActor : ReceiveActor {

        public static class Messages {

            public class SendHeadCommit {

            }
            /// <summary>
            /// Message sent to ask for a commit notification to be ordered
            /// </summary>
            public class SendCommitAfterCurrentHeadCheckpointMessage  {

                public long CurrentHeadCheckpoint { get; }
                public Option<int> NumberOfCommitsToSend { get; }

                [DebuggerStepThrough]
                public SendCommitAfterCurrentHeadCheckpointMessage(long currentHeadCheckpoint, Option<int> numberOfCommitsToSend)
                {
                    CurrentHeadCheckpoint = currentHeadCheckpoint;
                    NumberOfCommitsToSend = numberOfCommitsToSend;
                }
            }
        }
        private readonly IPersistStreams _persistStreams;
        private readonly IDbService _dbService;

        public EventStorePollerActor(IPersistStreams persistStreams, IDbService dbService) {
            _persistStreams = persistStreams;
            _dbService = dbService;
            Receive<Messages.SendCommitAfterCurrentHeadCheckpointMessage>(Received);
            Receive<Messages.SendHeadCommit>(Received);
        }

        private void Received(Messages.SendHeadCommit msg) {
            using (var con = _dbService.GetCommitStore()) {
                // get the head-1 checkpoint from the db
                var currentHeadCheckpoints = con.Query<long>("SELECT TOP 2 CheckpointToken FROM dbo.Commit ORDER BY CheckpointToken DESC").ToList();

                // reply with the head of the commit store
                var commit = _persistStreams.GetFrom(currentHeadCheckpoints.First()).First();
                Context.Sender.Tell(new OrderedCommitNotification(commit,currentHeadCheckpoints.Last()));
            }

            Context.Stop(Self);
        }

        private void Received(Messages.SendCommitAfterCurrentHeadCheckpointMessage msg) {
            long previousCheckpoint = msg.CurrentHeadCheckpoint;
            var commits = GetCommitsFromPersistentStore(msg);

            foreach (var commit in commits)
            {
                Context.Sender.Tell(new OrderedCommitNotification(commit, previousCheckpoint));
                previousCheckpoint = commit.CheckpointToken;
            }

            Context.Stop(Self);
        }

        private IEnumerable<ICommit> GetCommitsFromPersistentStore(Messages.SendCommitAfterCurrentHeadCheckpointMessage msg) {
            IEnumerable<ICommit> commits =_persistStreams.GetFrom(msg.CurrentHeadCheckpoint); //load all commits after checkpoint from db
            if (!msg.NumberOfCommitsToSend.HasValue)
                return commits;

            return commits.Take(msg.NumberOfCommitsToSend.Get());
        }
    }
}