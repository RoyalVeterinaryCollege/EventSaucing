using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using NEventStore.Persistence.Sql;

namespace EventSaucing.Storage.Sql {
    public class SqlDbService : IDbService, IConnectionFactory {
        private readonly string _connectionString;

        public SqlDbService(EventSaucingConfiguration configuration) {
            _connectionString = configuration.ConnectionString;
        }

        public DbConnection GetConnection(string connectionString) {
            return new SqlConnection(connectionString);
        }

        public DbConnection GetConnection() {
            return GetConnection(_connectionString);
        }

		//Required by NEvent
		IDbConnection IConnectionFactory.Open() {
			return new ConnectionScope(_connectionString, () => {
				var conn = GetConnection();
				conn.Open();
				return conn;
			});
		}

	    public Type GetDbProviderFactoryType() {
            //hard coded to sql server client
            return System.Data.SqlClient.SqlClientFactory.Instance.GetType();
        }
    }
}