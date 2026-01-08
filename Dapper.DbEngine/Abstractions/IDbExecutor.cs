using Dapper.DbEngine.Model;
using System.Data;
using System.Data.Common;

namespace Dapper.DbEngine.Abstractions
{
    public interface IDbExecutor
    {
        DbConnection Create();

        // Base Dapper
        Task<int> ExecuteSqlAsync(string sql, object? param, IDbTransaction? tran = null);

        // [Fix] 這裡必須加上 ? 變成 Task<T?>
        Task<T?> ExecuteScalarAsync<T>(string sql, object? param, IDbTransaction? tran = null);

        // [Fix] 這裡也必須加上 ? 變成 Task<T?>
        Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, IDbTransaction? tran = null);

        Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, IDbTransaction? tran = null);
        Task<T> QueryMultipleAsync<T>(string sql, object param, Func<SqlMapper.GridReader, Task<T>> projector, IDbTransaction? tran = null);
        Task<(IEnumerable<T> Items, int TotalCount)> QueryPagedAsync<T>(
        string sql,
        object? param,
        int page,
        int pageSize,
        string orderBy,
        IDbTransaction? tran = null);

        Task<TransactionResult> ExecuteInTransactionAsync(Func<DbTransaction, Task> action);
    }


}