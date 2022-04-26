using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.Storage;
using Microsoft.Extensions.Configuration;
using Scalesque;

namespace EventSaucing.StreamProcessors.Projectors {
    public class SqlProjectorCheckPointPersister : IStreamProcessorCheckpointPersister {
        private readonly IDbService _dbService;
        private readonly IConfiguration _config;

        public SqlProjectorCheckPointPersister(IDbService dbService, IConfiguration config) {
            _dbService = dbService;
            _config = config;
        }

        public virtual async Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor) {
            if (streamProcessor is SqlProjector sp) {
                using (var conn = sp.GetProjectionDb()) {
                    await conn.OpenAsync();

                    Option<long> persistedCheckpoint =
                        (await conn.QueryAsync<long>(
                            "SELECT LastCheckPointToken FROM dbo.StreamProcessorCheckpoints WHERE StreamProcessor = @StreamProcessor",
                            new { StreamProcessor = GetPersistedName(streamProcessor) })).HeadOption();

                    if (persistedCheckpoint.HasValue) {
                        return persistedCheckpoint.Get();
                    }

                    if (IsInitialisedAtHead(streamProcessor)) {
                        return await GetCommitstoreHeadAsync();
                    }

                    return 0L;
                }
            }
            else {
                throw new ArgumentException(
                    $"{nameof(SqlProjectorCheckPointPersister)} expects type of {nameof(SqlProjector)} but received  {streamProcessor.GetType()}");
            }
        }

        public virtual async Task<long> GetCommitstoreHeadAsync() {
            using (var conn = _dbService.GetCommitStore()) {
                await conn.OpenAsync();
                return await conn.ExecuteScalarAsync<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits");
            }
        }

        private bool IsInitialisedAtHead(StreamProcessor streamProcessor) {
            var strings = _config
                .GetSection("EventSaucing:Projectors:InitialiseAtHead")
                .Get<string[]>();
            if (strings is null) return false;
            return strings
                .Contains(streamProcessor.GetType().FullName);
        }

        public static string GetPersistedName(StreamProcessor streamProcessor) {
            var fullName = streamProcessor.GetType().FullName;
            return fullName.Length <= 800 ? fullName : fullName.Substring(0, 800);//only 800 characters for db persistence
        }

        const string SqlPersistProjectorState = @"
INSERT dbo.StreamProcessorCheckpoints (StreamProcessor, LastCheckpointToken)
SELECT @StreamProcessor, @Checkpoint
WHERE NOT EXISTS(SELECT 1 FROM dbo.StreamProcessorCheckpoints WHERE StreamProcessor = @StreamProcessor);
UPDATE dbo.StreamProcessorCheckpoints
    SET LastCheckpointToken = @Checkpoint 
WHERE StreamProcessor = @StreamProcessor;";

        public virtual async Task PersistCheckpointAsync(StreamProcessor streamProcessor, long checkpoint) {
            if (streamProcessor is SqlProjector sp) {
                using (var con = sp.GetProjectionDb()) {
                    await con.OpenAsync();
                    await con.ExecuteAsync(
                        SqlPersistProjectorState,
                        new { StreamProcessor = GetPersistedName(streamProcessor), streamProcessor.Checkpoint });
                }
            }
        }
    }
}