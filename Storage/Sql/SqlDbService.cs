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

        public IDbConnection GetConnection(string connectionString) {
            return new SqlConnection(connectionString);
        }

        public IDbConnection GetConnection() {
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
			var connection = GetConnection();
			return DbProviderFactories.GetFactory((DbConnection)connection).GetType();
		}
    }
}