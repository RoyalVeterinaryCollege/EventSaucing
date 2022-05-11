using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using EventSaucing.NEventStore;
using EventSaucing.Storage;
using NEventStore;
using NEventStore.Persistence;
using NEventStore.Persistence.Sql;
using Guid = System.Guid;

namespace EventSaucing.StreamProcessors.Projectors
{
    public abstract class AggregateGraphSqlProjector : SqlProjector
    {
        private readonly IDbService _dbService;
        public AggregateGraphSqlProjector(IPersistStreams persistStreams, IStreamProcessorCheckpointPersister checkpointPersister, IDbService dbService) : base(persistStreams, checkpointPersister) {
            _dbService = dbService;
        }

        public override async Task<bool> ProcessAsync(ICommit commit) {
            /* commented out to speed up catch up
            // make this idempotent
            using (var con = GetProjectionDb()) {
                await con.OpenAsync();

                var results = await con.QueryAsync<int>("SELECT TOP 1 1 FROM dbo.AggregateGraph WHERE CheckpointNumber = @CheckpointToken",
                    new { commit.CheckpointToken });
                if (results.Any()) return true;
            }*/

            var hasher = new Sha1StreamIdHasher();

            // find all the properties on the events which are Guids referring to aggregates in the commit store
            var aggregateProperties = commit.Events
                .Select(evt => evt.Body)
                .Select(evt => new {
                    Event = evt, Properties = evt.GetType()
                        .GetProperties()
                        .AsParallel()
                        .Where(propertyInfo => propertyInfo.PropertyType == typeof(Guid))
                        .Where(propertyInfo => {
                            // guard the guid corresponds to an aggregate
                            using (var con = _dbService.GetCommitStore()) {
                                con.Open();
                                var targetGuid = (Guid)propertyInfo.GetValue(evt);

                                var results = con.Query<int>(
                                    "SELECT TOP 1 1 FROM dbo.Commits WHERE BucketId=@BucketId AND StreamId = @StreamId AND StreamIdOriginal = @AggregateId",
                                    new { 
                                        BucketId = new DbString { Value = commit.BucketId, IsFixedLength = false, Length = 40, IsAnsi = true}, //nb IsAnsi seems to toggle to nvarchar/nchar vs varchar/nvarchar
                                        StreamId = new DbString { Value = hasher.GetHash(targetGuid.ToString()), IsFixedLength = true, Length = 40, IsAnsi = true },
                                        AggregateId = targetGuid.ToString() //this confirms existence as StreamId is a hash 
                                    }
                                );
                                return results.Any();
                            }
                        }).ToList()
                })
                .Where(a => a.Properties.Any()).ToList(); //guard there are properties which correspond to aggregates in the commit store
            
            //nothing to project
            if (!aggregateProperties.Any())
                return false;

            var sb = new StringBuilder(@"
INSERT INTO [dbo].[AggregateGraph]
	([SourceId]
	,[SourceStreamId]
	,[SourceAggregateType]
	,[EventName]
	,[Label]
	,[TargetId]
	,[TargetStreamId]
	,[CheckpointNumber])
VALUES ");

            var first = true;
            foreach (var eventAggregateProperties in aggregateProperties) {
                foreach (var propertyInfo in eventAggregateProperties.Properties) {
                    var sourceId = commit.AggregateId();
                    var sourceStreamId = hasher.GetHash(commit.AggregateId().ToString());
                    var sourceAggregateType = (string)commit.Headers["AggregateType"];
                    var eventName = eventAggregateProperties.Event.GetType().Name;
                    var label = propertyInfo.Name.Length < 500 ? propertyInfo.Name : propertyInfo.Name.Substring(0,500);
                    var targetId = (Guid)propertyInfo.GetValue(eventAggregateProperties.Event);
                    var targetStreamId = hasher.GetHash(targetId.ToString());
                    var checkpointNumber = commit.CheckpointToken;
                    sb.AppendLine($"{(!first ? "," : "")}('{sourceId}','{sourceStreamId}','{sourceAggregateType}', '{eventName}', '{label}','{targetId}', '{targetStreamId}', {checkpointNumber})");
                    first = false;
                }
            }

            using (var con = GetProjectionDb()) {
                await con.OpenAsync();
                await con.ExecuteAsync(sb.ToString());
            }

            return true;
        }
    }
}
