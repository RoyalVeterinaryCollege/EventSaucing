using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.Storage;
using Scalesque;

namespace EventSaucing.StreamProcessors.Projectors {
    public static class ProjectorHelper {
        /// <summary>
        /// Gets the unique ProjectorId of a projector
        /// </summary>
        /// <param name="projectorBase"></param>
        /// <exception cref="ArgumentException">Thrown if the attribute is mssing</exception>
        /// <returns></returns>
        public static int GetProjectorId(this LegacyProjector projectorBase) {
            return GetProjectorId(projectorBase.GetType());
        }

        /// <summary>
        ///     Gets the uniqueprojectorId of a projector
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the attribute is missing</exception>
        /// <returns></returns>
        public static int GetProjectorId(this Type projectorType) {
            return projectorType.GetCustomAttributes(false)
                                .FlatMap(x => x.To<ProjectorAttribute>())
                                .HeadOption()
                                .Map(x => x.ProjectorId)
                                .GetOrElse(
                                    () => {
                                        throw new ArgumentException("projector doesn't have the ProjectorAttribute");
                                    });
        }

        /// <summary>
        ///     NEventStore uses checkpoint tokens typed as strings
        /// </summary>
        /// <param name="checkpoint"></param>
        /// <returns></returns>
        public static string ToCheckpointToken(this Option<long> checkpoint) => checkpoint.Map(x => x.ToString()).GetOrElse(() => null);


        const string SqlPersistProjectorState = @"
			MERGE dbo.ProjectorStatus AS target
			USING (SELECT @ProjectorId, @ProjectorName, @LastCheckpointToken) AS source (ProjectorId, ProjectorName, LastCheckpointToken)
			ON (target.ProjectorId = source.ProjectorId)
			WHEN MATCHED THEN 
				UPDATE SET LastCheckpointToken = source.LastCheckpointToken
			WHEN NOT MATCHED THEN	
				INSERT (ProjectorId, ProjectorName, LastCheckpointToken)
				VALUES (source.ProjectorId, source.ProjectorName, source.LastCheckpointToken);";

        /// <summary>
        /// Persists the projector's current checkpoint in the db (in scope of tx)
        /// </summary>
        /// <param name="projector"></param>
        /// <param name="tx"></param>
        public static void PersistProjectorCheckpoint(this LegacyProjector projector, IDbTransaction tx) {
            var sqlParams = GetProjectorParams(projector);
            tx.Connection.Execute(SqlPersistProjectorState, sqlParams, tx);
        }


		const string SqlInitialiseStreamProcessorCheckpointsTable = @"
-- get an exclusive lock on creating this table. It's possible for multiple nodes to start up at the same time and each will try to create this table
-- @result = 0 = we got the lock
-- @result = 1 = we got the lock after someone released it
-- @result < 0 = we didn't get the lock

EXEC @result = sp_getapplock @resource='dbo.StreamProcessorCheckpoints', @lockmode='Exclusive', @LockOwner='Session', @LockTimeout=0;

IF (@result >=0 AND NOT EXISTS (SELECT * 
     FROM INFORMATION_SCHEMA.TABLES 
     WHERE TABLE_SCHEMA = 'dbo' 
     AND TABLE_NAME = 'StreamProcessorCheckpoints'))
BEGIN
	CREATE TABLE [dbo].[StreamProcessorCheckpoints](
		[StreamProcessor] [nvarchar](800) NOT NULL,
		[LastCheckpointToken] [bigint] NOT NULL,
		CONSTRAINT [PK_StreamProcessor] PRIMARY KEY CLUSTERED 
		(
			[StreamProcessor] ASC
		) ON [PRIMARY]
	) ON [PRIMARY]
END

IF (@result >=0 )
BEGIN
    sp_releaseapplock @Resource = 'dbo.StreamProcessorCheckpoints';
END
";

        /// <summary>
        /// Creates the dbo.ProjectorStatus table. Thread safe and safe for multiple replicas to call at once.
        /// </summary>
        /// <param name="connection">DbConnection an unopened db connection pointing to the db where the table should be created</param>
        /// <returns></returns>
		public static async Task InitialiseProjectorStatusStore(DbConnection connection) {
			await connection.OpenAsync();
			await connection.ExecuteAsync(SqlInitialiseStreamProcessorCheckpointsTable);
		}

        private static object GetProjectorParams(LegacyProjector projector) {
            return new {
                ProjectorId = projector.ProjectorId,
                ProjectorName = projector.GetType().Name,
                LastCheckpointToken = projector.Checkpoint
            };
        }

        /// <summary>
        /// Persists the projector's current checkpoint in the db (no tx)
        /// </summary>
        /// <param name="projector"></param>
        /// <param name="conn"></param>
        public static void PersistProjectorCheckpoint(this LegacyProjector projector, IDbConnection conn) {
            var sqlParams = GetProjectorParams(projector);
            conn.Execute(SqlPersistProjectorState, sqlParams);
        }
    }
}