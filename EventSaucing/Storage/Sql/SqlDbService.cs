using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using NEventStore.Persistence.Sql;

namespace EventSaucing.Storage.Sql {
    public class SqlDbService : IDbService, IConnectionFactory {
        private readonly string _readmodelConnectionString;
        private readonly string _commitStoreConnectionString;

        public SqlDbService(EventSaucingConfiguration configuration) {
            this._readmodelConnectionString = configuration.ReadmodelConnectionString;
            this._commitStoreConnectionString = configuration.CommitStoreConnectionString;
        }

        public DbConnection GetConnection(string connectionString) {
            return new SqlConnection(connectionString);
        }

        public DbConnection GetReadmodel() {
            return GetConnection(_readmodelConnectionString);
        }

        public DbConnection GetCommitStore() {
            return GetConnection(_commitStoreConnectionString);
        }

        public DbConnection GetConnection() {
            return GetReadmodel();
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