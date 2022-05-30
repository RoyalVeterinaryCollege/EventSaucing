using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.Storage;
using Scalesque;

namespace EventSaucing.StreamProcessors.Projectors {
    /// <summary>
    /// Represents a way of initialising a StreamProcessor
    /// </summary>
    public abstract class InitialisationOption {
        public abstract Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor,IDbService dbService);
    }
    /// <summary>
    /// Gets the checkpoint from the dbo.StreamProcessorCheckpoints table
    /// </summary>
    public class PersistedSqlProjectorCheckpoint : InitialisationOption {
        public override async Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor, IDbService dbService) {
            if (streamProcessor is SqlProjector sp) {
                using (var conn = sp.GetProjectionDb()) {
                    await conn.OpenAsync();

                    var args = new {
                        StreamProcessor = SqlProjectorCheckPointPersister.GetPersistedName(streamProcessor)
                    };
                    Option<long> persistedCheckpoint =
                        (await conn.QueryAsync<long>(
                            "SELECT LastCheckPointToken FROM dbo.StreamProcessorCheckpoints WHERE StreamProcessor = @StreamProcessor",
                            args))
                        .HeadOption();

                    return persistedCheckpoint;
                }
            }
            else {
                return Option.None();
            }
        }
    }
    /// <summary>
    /// Gets the last Checkpoint from the commit store
    /// </summary>
    public class HeadOfCommitStore : InitialisationOption {
        public override async Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor,
            IDbService dbService) {
            using (var conn = dbService.GetCommitStore()) {
                await conn.OpenAsync();
                return (await conn.QueryAsync<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits")).HeadOption();
            }
        }
    }
    /// <summary>
    /// Gets the first commit from the commit store
    /// </summary>
    public class FirstCommit : InitialisationOption {
        public override Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor,
            IDbService dbService) {
            return Task.FromResult(Option.Some(0L));
        }
    }

    /// <summary>
    /// You can declare a series of options for initialising the StreamProcessor.  The first to return a Some will be the value chosen.
    /// </summary>
    public class DeclarativeCheckpointPersister : SqlProjectorCheckPointPersister  {
        private readonly IDbService _dbService;
        private List<InitialisationOption> options = new List<InitialisationOption>();

        public DeclarativeCheckpointPersister(IDbService dbService):base() {
            _dbService = dbService;
        }

        public DeclarativeCheckpointPersister TryInitialiseFrom<T>(T initialisationOption) where T : InitialisationOption {
            options.Add(initialisationOption);
            return this;
        }

        public DeclarativeCheckpointPersister TryInitialiseFrom<T>() where T : InitialisationOption, new() {
            options.Add(new T());
            return this;
        }

        public override async Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor) {
            foreach (var option in options) {
                Option<long> result = await option.GetInitialCheckpointAsync(streamProcessor, _dbService);
                if (result.HasValue) return result.Get();
            }

            //default to first commit
            return 0L;
        }
    }
}