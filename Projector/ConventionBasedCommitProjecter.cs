using System;
using EventSaucing.Storage;
using NEventStore;

namespace EventSaucing.Projector {
    /// <summary>
    /// A conventional way of projecting commits.  This handles ACIDic projection of all projectable events in the commit.
    /// </summary>
    public class ConventionBasedCommitProjecter {
        private readonly ProjectorBase _projector;
        private readonly IDbService _dbService;
        private readonly ConventionBasedEventDispatcher _dispatcher;
        private readonly Random _rnd;

        public ConventionBasedCommitProjecter(ProjectorBase projector, IDbService dbService, ConventionBasedEventDispatcher dispatcher) {
            _projector = projector;
            _dbService = dbService;
            _dispatcher = dispatcher;
            _rnd = new Random();
        }

        public void Project(ICommit commit) {
            if (_dispatcher.CanProject(commit)) {
                using (var conn = _dbService.GetConnection()) {
                    conn.Open();
                    using (var tx = conn.BeginTransaction()) {
                        _dispatcher.Project(tx, commit);
                        _projector.PersistProjectorCheckpoint(tx);
                        tx.Commit();
                    }
                    conn.Close();
                }
            } else {
                _dispatcher.AdvanceProjectorCheckpoint(commit);
                //only randomly persist projector state if there are no events to project in this commit (1% of the time). 
                //this speeds up catchups
                if (_rnd.Next(0, 99) == 0) { 
                    using (var conn = _dbService.GetConnection()) {
                        conn.Open();
                        _projector.PersistProjectorCheckpoint(conn);
                        conn.Close();
                    }
                }
            }
        }
    }
}