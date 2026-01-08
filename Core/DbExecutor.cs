// [Change] 移除 Dapper.Contrib，改用我們自定義且支援 Dialect 的擴充
using Core.Infrastructure.Data.Extensions;
using Dapper;
using Dapper.DbEngine.Abstractions;
using Dapper.DbEngine.Exceptions;
using Dapper.DbEngine.Model;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace Core.Infrastructure.Data.Core
{
    public class DbExecutor : IDbExecutor
    {
        private readonly string _connectionString;
        private readonly Func<string, DbConnection> _connectionFactory;
        private readonly ILogger<DbExecutor> _logger;

        public enum TransactionStatus
        {
            Success,
            DuplicateKey, // 對應 SQL 2627/2601
            SystemError   // 其他未預期錯誤
        }

        // 若要徹底解耦資料庫類型 (SQL Server / Postgres)，建議注入 Func<DbConnection> factory
        public DbExecutor(string connectionString, Func<string, DbConnection> connectionFactory, ILogger<DbExecutor> logger)
        {
            _connectionString = connectionString;
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        // [Note] 若切換至 Postgres，此處需改為 new NpgsqlConnection(_connectionString)
        public DbConnection Create() => _connectionFactory(_connectionString);

        // =================================================================================
        // 私有策略核心 (保持不變，邏輯穩健)
        // =================================================================================

        private async Task<TResult> ExecuteStrategyAsync<TResult>(
            IDbTransaction? tran, // 允許 null
            Func<DbConnection, IDbTransaction?, Task<TResult>> operation,
            string sqlForLog = "")
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (tran != null)
                {
                    if (tran.Connection == null) throw new InvalidOperationException("Transaction is disconnected.");
                    return await operation((DbConnection)tran.Connection, tran);
                }

                await using var conn = Create();
                await conn.OpenAsync();
                return await operation(conn, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL Error. Duration: {Elapsed}ms. SQL: {Sql}", sw.ElapsedMilliseconds, sqlForLog);
                throw new DbOperationException(
                    $"Database operation failed after {sw.ElapsedMilliseconds}ms.",
                    sqlForLog,
                    sw.ElapsedMilliseconds,
                    ex
                );
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds > 1000)
                {
                    _logger.LogWarning("Slow Query ({Elapsed}ms): {Sql}", sw.ElapsedMilliseconds, sqlForLog);
                }
            }
        }

        // =================================================================================
        // 公開實作 (已全面改用 DapperExtensions)
        // =================================================================================      

        public Task<int> ExecuteSqlAsync(string sql, object? param, IDbTransaction? tran = null)
            => ExecuteStrategyAsync(tran, (conn, t) => conn.ExecuteAsync(sql, param, t), sql);

        public Task<T?> ExecuteScalarAsync<T>(string sql, object? param, IDbTransaction? tran = null)
            => ExecuteStrategyAsync(tran, (conn, t) => conn.ExecuteScalarAsync<T>(sql, param, t), sql);

        public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, IDbTransaction? tran = null)
            => ExecuteStrategyAsync(tran, (conn, t) => conn.QueryFirstOrDefaultAsync<T>(sql, param, t), sql);

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, IDbTransaction? tran = null)
        {
            return ExecuteStrategyAsync(tran, async (conn, t) =>
            {
                return await conn.QueryAsync<T>(sql, param, t);
            }, sql);
        }

        public Task<T> QueryMultipleAsync<T>(string sql, object param, Func<SqlMapper.GridReader, Task<T>> projector, IDbTransaction? tran = null)
        {
            return ExecuteStrategyAsync(tran, async (conn, t) =>
            {
                using var grid = await conn.QueryMultipleAsync(sql, param, t);
                return await projector(grid);
            }, sql);
        }

        public Task<(IEnumerable<T> Items, int TotalCount)> QueryPagedAsync<T>(
        string sql,
        object? param,
        int page,
        int pageSize,
        string orderBy,
        IDbTransaction? tran = null)
        {
            // 使用你的 ExecuteStrategyAsync 來管理連線與錯誤 Log
            return ExecuteStrategyAsync(tran, async (conn, t) =>
            {
                return await conn.QueryPagedAsync<T>(sql, param, page, pageSize, orderBy, t);
            }, sql);
        }

        public async Task<TransactionResult> ExecuteInTransactionAsync(Func<DbTransaction, Task> action)
        {
            var sw = Stopwatch.StartNew();
            await using var conn = Create();
            await conn.OpenAsync();
            await using var tran = await conn.BeginTransactionAsync();

            try
            {
                await action(tran);
                await tran.CommitAsync();
                return TransactionResult.Ok();
            }
            catch (Exception ex)
            {
                try { await tran.RollbackAsync(); } catch { /* log ignore */ }

                _logger.LogError(ex, "Transaction Failed. Duration: {Elapsed}ms", sw.ElapsedMilliseconds);

                // 捕捉到未預期錯誤 -> 轉換為 SystemError 狀態
                // 注意：這裡不 throw，而是回傳 Error 物件
                return TransactionResult.Error("An unexpected error occurred.");
            }
        }
    }
}