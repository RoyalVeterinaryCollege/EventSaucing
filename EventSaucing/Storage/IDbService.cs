using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;

namespace EventSaucing.Storage {
    public interface IDbService {
		/// <summary>
		/// Gets a connection to the replica db
		/// </summary>
		/// <returns></returns>
		DbConnection GetReplica();
		/// <summary>
		/// Gets a connection to the commit store db
		/// </summary>
		/// <returns></returns>
        DbConnection GetCommitStore();

		/// <summary>
		/// Legacy. Gets connection to db.
		/// </summary>
		/// <returns></returns>
        [Obsolete("Replace all usage with GetReplica(), GetCommitStore(), or GetCluster() as appropriate")]
		DbConnection GetConnection();

		/// <summary>
		/// Gets the cluster db shared by all replicas
		/// </summary>
		/// <returns></returns>
        DbConnection GetCluster();
    }
}