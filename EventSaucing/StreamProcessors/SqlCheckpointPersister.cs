using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Scalesque;

namespace EventSaucing.StreamProcessors {
    public class SqlCheckpointPersister : IStreamProcessorCheckpointPersister {
        private Func<DbConnection> getCheckpointDb;

        public SqlCheckpointPersister(Func<DbConnection> getCheckpointDb) {
            this.getCheckpointDb = getCheckpointDb;
        }

        public virtual async Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor) {
            using (var conn = getCheckpointDb()) {
                await conn.OpenAsync();

                Option<long> persistedCheckpoint =
                    (await conn.QueryAsync<long>(
                        "SELECT LastCheckPointToken FROM dbo.StreamProcessorCheckpoints WHERE StreamProcessor = @StreamProcessor",
                        new { StreamProcessor = GetPersistedName(streamProcessor) })).HeadOption();

                if (persistedCheckpoint.HasValue) {
                    return persistedCheckpoint.Get();
                }

                return 0L;
            }
		}

		public static string GetPersistedName(StreamProcessor streamProcessor) {
            return GetPersistedName(streamProcessor.GetType());
		}

		public static string GetPersistedName(Type type) {
			var fullName = type.FullName;
			return fullName.Length <= 800 ? fullName : fullName.Substring(0, 800); // only 800 characters for db persistence
		}

		const string SqlPersistProjectorState = @"
INSERT dbo.StreamProcessorCheckpoints (StreamProcessor, LastCheckpointToken)
SELECT @StreamProcessor, @Checkpoint
WHERE NOT EXISTS(SELECT 1 FROM dbo.StreamProcessorCheckpoints WHERE StreamProcessor = @StreamProcessor);
IF (@@ROWCOUNT = 0)
BEGIN
    UPDATE dbo.StreamProcessorCheckpoints SET LastCheckpointToken = @Checkpoint WHERE StreamProcessor = @StreamProcessor;
END;";

        public virtual async Task PersistCheckpointAsync(StreamProcessor streamProcessor, long checkpoint) {
            using (var con = getCheckpointDb()) {
                await con.OpenAsync();
                await con.ExecuteAsync(
                    SqlPersistProjectorState,
                    new { StreamProcessor = GetPersistedName(streamProcessor), streamProcessor.Checkpoint });
            }
        }
    }
}
