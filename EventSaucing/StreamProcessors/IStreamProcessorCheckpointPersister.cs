﻿using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.Storage;
using EventSaucing.StreamProcessors.Projectors;
using Microsoft.Extensions.Configuration;
using Scalesque;

namespace EventSaucing.StreamProcessors {
    public interface IStreamProcessorCheckpointPersister {
        //Task<long> GetCommitstoreHeadAsync();
        Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor);
        Task PersistCheckpointAsync(StreamProcessor streamProcessor, long checkpoint);
    }

    public class LegacyProjectorCheckpointPersister : IStreamProcessorCheckpointPersister {
        private readonly IDbService _dbService;
        private readonly IConfiguration _config;

        public LegacyProjectorCheckpointPersister(IDbService dbService, IConfiguration config) {
            _dbService = dbService;
            _config = config;
        }

        public async Task<long> GetCommitstoreHeadAsync() {
            using (var conn = _dbService.GetCommitStore()) {
                await conn.OpenAsync();

                return await conn.ExecuteScalarAsync<long>("SELECT MAX(CheckpointNumber) FROM dbo.Commits");
            }
        }

        private int GetProjectorId(StreamProcessor sp) =>
            sp is LegacyProjector projector
                ? projector.GetProjectorId()
                : ThrowNeedLegacyProjector(sp);

        private static int ThrowNeedLegacyProjector(StreamProcessor sp) =>
            throw new ArgumentException(
                $"{nameof(LegacyProjectorCheckpointPersister)} expects type of {nameof(LegacyProjector)} but received  {sp.GetType()}");

        public async Task<long> GetInitialCheckpointAsync(StreamProcessor streamProcessor) {
            //get the persisted checkpoint (if there is one)
            using (var conn = _dbService.GetReplica()) {
                await conn.OpenAsync();

                Option<long> persistedCheckpoint = (await
                    conn.QueryAsync<long>(
                        "SELECT LastCheckPointToken FROM dbo.ProjectorStatus WHERE ProjectorId = @ProjectorId",
                        new { ProjectorId = GetProjectorId(streamProcessor) })).HeadOption();
                if (persistedCheckpoint.HasValue) {
                    return persistedCheckpoint.Get();
                }

                if (IsInitialisedAtHead(streamProcessor)) {
                    return await GetCommitstoreHeadAsync();
                }

                return 0L;
            }
        }

        private bool IsInitialisedAtHead(StreamProcessor streamProcessor) {
            return _config
                .GetSection("EventSaucing:Projectors:InitialiseAtHead")
                .Get<string[]>()
                .Contains(streamProcessor.GetType().FullName);
        }

        const string SqlPersistProjectorState = @"
			MERGE dbo.ProjectorStatus AS target
			USING (SELECT @ProjectorId, @ProjectorName, @LastCheckpointToken) AS source (ProjectorId, ProjectorName, LastCheckpointToken)
			ON (target.ProjectorId = source.ProjectorId)
			WHEN MATCHED THEN 
				UPDATE SET LastCheckpointToken = source.LastCheckpointToken
			WHEN NOT MATCHED THEN	
				INSERT (ProjectorId, ProjectorName, LastCheckpointToken)
				VALUES (source.ProjectorId, source.ProjectorName, source.LastCheckpointToken);";

        public async Task PersistCheckpointAsync(StreamProcessor streamProcessor, long checkpoint) {
            if (streamProcessor is LegacyProjector projector) {
                var sqlParams = (object)new {
                    ProjectorId = projector.ProjectorId,
                    ProjectorName = projector.GetType().Name,
                    LastCheckpointToken = projector.Checkpoint
                };
                using (var con = _dbService.GetReplica()) {
                    await con.OpenAsync();
                    await con.ExecuteAsync(SqlPersistProjectorState, sqlParams);
                }
            }
            else {
                ThrowNeedLegacyProjector(streamProcessor);
            }
        }
    }

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
                            "SELECT LastCheckPointToken FROM dbo.ProjectorStatus WHERE Fullname = @FullName",
                            new { FullName = GetPersistedName(streamProcessor) })).HeadOption();

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
			MERGE dbo.StreamProcessorStatus AS target
			USING (SELECT @FullName, @Checkpoint) AS source (FullName, Checkpoint)
			ON (target.FullName = source.FullName)
			WHEN MATCHED THEN 
				UPDATE SET LastCheckpointToken = source.Checkpoint
			WHEN NOT MATCHED THEN	
				INSERT (FullName, LastCheckpointToken)
				VALUES (source.FullName, source.Checkpoint);";

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