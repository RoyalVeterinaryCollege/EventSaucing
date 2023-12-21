using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Dapper;
using EventSaucing.HostedServices;
using EventSaucing.Storage;
using Microsoft.Extensions.Logging;
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

                [DebuggerStepThrough]
                public SendCommitAfterCurrentHeadCheckpointMessage(long currentHeadCheckpoint){
                    CurrentHeadCheckpoint = currentHeadCheckpoint;
                }
            }
        }
        private readonly IPersistStreams _persistStreams;
        private readonly IDbService _dbService;
        private readonly ILogger<EventStorePollerActor> _logger;

        public EventStorePollerActor(IPersistStreams persistStreams, IDbService dbService, ILogger<EventStorePollerActor> logger) {
            _persistStreams = persistStreams;
            _dbService = dbService;
            _logger = logger;
            Receive<Messages.SendCommitAfterCurrentHeadCheckpointMessage>(Received);
            Receive<Messages.SendHeadCommit>(Received);
        }

        private void Received(Messages.SendHeadCommit msg) {
            _logger.LogDebug("Received SendHeadCommit message");
            using (var con = _dbService.GetCommitStore()) {
                // get the head-1 checkpoint from the db
                var currentHeadCheckpoints = con.Query<long>("SELECT TOP 2 CheckpointNumber FROM dbo.Commits ORDER BY CheckpointNumber DESC").ToList();

                // reply with the head of the commit store
                var commit = _persistStreams.GetFrom(currentHeadCheckpoints.Last()).First();
                Context.Sender.Tell(new OrderedCommitNotification(commit,currentHeadCheckpoints.Last()));
            }

            Context.Stop(Self);
        }

        private void Received(Messages.SendCommitAfterCurrentHeadCheckpointMessage msg) {
            _logger.LogDebug($"Received SendCommitAfterCurrentHeadCheckpointMessage message @ {msg.CurrentHeadCheckpoint}");
            long previousCheckpoint = msg.CurrentHeadCheckpoint;
            var commits = GetCommitsFromPersistentStore(msg);

            foreach (var commit in commits) {
                Context.Sender.Tell(new OrderedCommitNotification(commit, previousCheckpoint));
                previousCheckpoint = commit.CheckpointToken;
            }

            Context.Stop(Self);
        }

        private IEnumerable<ICommit> GetCommitsFromPersistentStore(Messages.SendCommitAfterCurrentHeadCheckpointMessage msg) {
            return _persistStreams.GetFrom(msg.CurrentHeadCheckpoint); //load all commits after checkpoint from db
        }
    }
}