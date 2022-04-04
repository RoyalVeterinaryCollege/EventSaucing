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

        public SqlProjectorCheckPointPersister(IDbService dbService,
            IConfiguration config) {
            _dbService = dbService;
            _config = config;
        }

        public async Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor) {
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

        public async Task<long> GetCommitstoreHeadAsync() {
            using (var conn = _dbService.GetCommitStore()) {
                await conn.OpenAsync();
                return await conn.ExecuteScalarAsync<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits");
            }
        }

        private bool IsInitialisedAtHead(StreamProcessor streamProcessor) {
            return _config
                .GetSection("EventSaucing:Projectors:InitialiseAtHead")
                .Get<string[]>()
                .Contains(streamProcessor.GetType().FullName);
        }

        private static string GetPersistedName(StreamProcessor streamProcessor) {
            return streamProcessor.GetType().FullName.Substring(0, 800); //only 800 characters for db persistence
        }

        const string SqlPersistProjectorState = @"
			MERGE dbo.StreamProcessorCheckpoints AS target
			USING (SELECT @StreamProcessor, @Checkpoint) AS source (StreamProcessor, Checkpoint)
			ON (target.StreamProcessor = source.StreamProcessor)
			WHEN MATCHED THEN 
				UPDATE SET LastCheckpointToken = source.Checkpoint
			WHEN NOT MATCHED THEN	
				INSERT (StreamProcessor, LastCheckpointToken)
				VALUES (source.StreamProcessor, source.Checkpoint);";

        public async Task PersistCheckpointAsync(StreamProcessor streamProcessor, long checkpoint) {
            if (streamProcessor is SqlProjector sp) {
                using (var con = sp.GetProjectionDb()) {
                    await con.OpenAsync();
                    await con.ExecuteAsync(
                        SqlPersistProjectorState,
                        new { FullName = GetPersistedName(streamProcessor), streamProcessor.Checkpoint });
                }
            }
        }
    }
}