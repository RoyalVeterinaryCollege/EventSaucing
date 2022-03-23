using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;

namespace EventSaucing.Storage {
    public interface IDbService {
		/// <summary>
		/// Gets a connection to the readmodel db
		/// </summary>
		/// <returns></returns>
		DbConnection GetReadmodel();
		/// <summary>
		/// Gets a connection to the commit store db
		/// </summary>
		/// <returns></returns>
        DbConnection GetCommitStore();

		/// <summary>
		/// Legacy. Get's connection to db.
		/// </summary>
		/// <returns></returns>
        [Obsolete("Replace all usage with GetReadmodel or GetCommitStore as appropriate")]
		DbConnection GetConnection();
    }
}