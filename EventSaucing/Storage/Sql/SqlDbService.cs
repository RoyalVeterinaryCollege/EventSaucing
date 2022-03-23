using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using NEventStore.Persistence.Sql;

namespace EventSaucing.Storage.Sql {
    public class SqlDbService : IDbService, IConnectionFactory {
        private readonly string _replicaConnectionString;
        private readonly string _commitStoreConnectionString;

        public SqlDbService(EventSaucingConfiguration configuration) {
            this._replicaConnectionString = configuration.ReplicaConnectionString;
            this._commitStoreConnectionString = configuration.CommitStoreConnectionString;
        }

        public DbConnection GetConnection(string connectionString) {
            return new SqlConnection(connectionString);
        }

        public DbConnection GetReplica() {
            return GetConnection(_replicaConnectionString);
        }

        public DbConnection GetConnection() {
            return GetReplica();
        }

        //Required by NEvent
        IDbConnection IConnectionFactory.Open() {
			var conn = GetConnection(_commitStoreConnectionString);
			conn.Open();
			return conn;
		}

	    public Type GetDbProviderFactoryType() {
            //hard coded to sql server client
            return System.Data.SqlClient.SqlClientFactory.Instance.GetType();
        }
    }
}