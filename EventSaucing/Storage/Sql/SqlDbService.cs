using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using NEventStore.Persistence.Sql;

namespace EventSaucing.Storage.Sql {
    public class SqlDbService : IDbService, IConnectionFactory {
        private readonly string _replicaConnectionString;
        private readonly string _commitStoreConnectionString;
        private readonly string _clusterConnectionString;

        public SqlDbService(EventSaucingConfiguration configuration) {
            this._replicaConnectionString = configuration.ReplicaConnectionString;
            this._commitStoreConnectionString = configuration.CommitStoreConnectionString;
            this._clusterConnectionString = configuration.ClusterConnectionString;

        }

        public DbConnection GetConnection(string connectionString) {
            return new SqlConnection(connectionString);
        }

        public DbConnection GetReplica() {
            return GetConnection(_replicaConnectionString);
        }

        public DbConnection GetCommitStore() {
            return GetConnection(_commitStoreConnectionString);
        }

        public DbConnection GetConnection() {
            return GetReplica();
        }

        public DbConnection GetCluster() {
            return GetConnection(_clusterConnectionString);
        }

        //Required by NEvent
        IDbConnection IConnectionFactory.Open() {
            var conn = GetCommitStore();
			conn.Open();
			return conn;
		}

	    public Type GetDbProviderFactoryType() {
            //hard coded to sql server client
            return System.Data.SqlClient.SqlClientFactory.Instance.GetType();
        }
    }
}