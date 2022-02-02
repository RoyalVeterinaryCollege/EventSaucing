using System.Data;
using System.Data.Common;
using System.Data.SqlClient;

namespace EventSaucing.Storage {
    public interface IDbService {
		/// <summary>
		/// Gets a connection to the db storage.
		/// </summary>
		/// <returns></returns>
		DbConnection GetConnection();
    }
}