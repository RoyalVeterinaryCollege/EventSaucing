using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.Storage;
using Scalesque;

namespace EventSaucing.StreamProcessors {

    /// <summary>
    /// Represents a way of initialising a StreamProcessor
    /// </summary>
    public abstract class InitialisationOption {
        public abstract Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor, IDbService dbService, Func<DbConnection> getCheckpointDb);
	}

	/// <summary>
	/// Gets the checkpoint from the dbo.StreamProcessorCheckpoints table
	/// </summary>
	public class PersistedSqlCheckpoint : InitialisationOption {

		public override async Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor, IDbService dbService, Func<DbConnection> getCheckpointDb) {

			using (var conn = getCheckpointDb()) {

				await conn.OpenAsync();

				var args = new {
					StreamProcessor = SqlCheckpointPersister.GetPersistedName(streamProcessor)
				};

				Option<long> persistedCheckpoint =
					(await conn.QueryAsync<long>("SELECT LastCheckPointToken FROM dbo.StreamProcessorCheckpoints WHERE StreamProcessor = @StreamProcessor", args)).HeadOption();

				return persistedCheckpoint;
			}
		}
	}

	/// <summary>
	/// Gets the checkpoint of a different stream projector from the dbo.StreamProcessorCheckpoints table
	/// </summary>
	public class OtherPersistedSqlCheckpoint : InitialisationOption {

        private readonly Type streamProcessorType;
        private readonly bool mustBePersisted;

        public OtherPersistedSqlCheckpoint(Type streamProcessorType, bool mustBePersisted) {
            if (!streamProcessorType.IsSubclassOf(typeof(StreamProcessor))) {
                throw new ArgumentException($"{streamProcessorType.FullName} must be a subclass of StreamProcessor");
            }

            this.streamProcessorType = streamProcessorType;
            this.mustBePersisted = mustBePersisted;
		}

		public override async Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor, IDbService dbService, Func<DbConnection> getCheckpointDb) {

			using (var conn = getCheckpointDb()) {

				await conn.OpenAsync();

				var args = new {
					StreamProcessor = SqlCheckpointPersister.GetPersistedName(streamProcessorType)
				};

				Option<long> persistedCheckpoint =
					(await conn.QueryAsync<long>("SELECT LastCheckPointToken FROM dbo.StreamProcessorCheckpoints WHERE StreamProcessor = @StreamProcessor", args)).HeadOption();

                if (mustBePersisted && !persistedCheckpoint.HasValue) {
					throw new KeyNotFoundException($"StreamProcessor {streamProcessorType.FullName} must have a persisted checkpoint");
				}

				return persistedCheckpoint;
			}
		}
	}

    /// <summary>
    /// Gets the last Checkpoint from the commit store
    /// </summary>
    public class HeadOfCommitStore : InitialisationOption {

        public override async Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor, IDbService dbService, Func<DbConnection> getCheckpointDb) {
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

        public override Task<Option<long>> GetInitialCheckpointAsync(StreamProcessor streamProcessor, IDbService dbService, Func<DbConnection> getCheckpointDb) {
            return Task.FromResult(Option.Some(0L));
        }
    }

    /// <summary>
    /// Declare one or more options for initialising the StreamProcessor. The first to return a result will be used
    /// </summary>
    public class DeclarativeCheckpointPersister : SqlCheckpointPersister {
        private readonly IDbService dbService;
        private readonly Func<DbConnection> getCheckpointDb;
        private readonly List<InitialisationOption> options = new List<InitialisationOption>();

        public DeclarativeCheckpointPersister(IDbService dbService, Func<DbConnection> getCheckpointDb) : base(getCheckpointDb) {
            this.dbService = dbService;
			this.getCheckpointDb = getCheckpointDb;
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
                Option<long> result = await option.GetInitialCheckpointAsync(streamProcessor, dbService, getCheckpointDb);
                if (result.HasValue) return result.Get();
            }

            //default to first commit
            return 0L;
        }
    }
}
