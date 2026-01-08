using Dapper;
using Dapper.DbEngine.Abstractions;
using Dapper.DbEngine.Dialects;
using Dapper.DbEngine.Internals; // 引用統一的中介資料層
using System.Data;

namespace Core.Infrastructure.Data.Extensions
{
    public static class DapperExtensions
    {
        // 全域配置：預設使用 SQL Server
        public static ISqlDialect Dialect { get; set; } = new SqlServerDialect();

        // ------------------------------------------------------------------------
        // CRUD Operations
        // ------------------------------------------------------------------------

        public static async Task<T?> GetByIdAsync<T, TKey>(this IDbConnection conn, TKey id, IDbTransaction? tx = null) where T : class
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();

            if (meta.KeyColumn == null)
                throw new InvalidOperationException($"Type {typeof(T).Name} has no Key defined.");

            // 使用 Parameters 避免 SQL Injection 與 Boxing
            var sql = $"SELECT * FROM {meta.FormattedTableName} WHERE {meta.KeyColumn.DbColQuoted} = {Dialect.ParameterPrefix}Id";
            return await conn.QueryFirstOrDefaultAsync<T>(sql, new { Id = id }, tx);
        }

        public static async Task<long> InsertAsync<T>(this IDbConnection conn, T entity, IDbTransaction? tx = null) where T : class
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();

            // 情境 A: 有 Key 且非 Explicit (Identity) -> 執行 Insert 並回填 ID
            if (meta.KeyColumn != null && !meta.KeyColumn.IsExplicitKey)
            {
                var result = await conn.ExecuteScalarAsync<object>(meta.InsertSqlAndReturnId, entity, tx);
                long newId = result != null ? Convert.ToInt64(result) : 0;

                if (newId > 0 && meta.KeyColumn.CanWrite)
                {
                    var prop = meta.KeyColumn.PropertyInfo;
                    var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    prop.SetValue(entity, Convert.ChangeType(newId, underlyingType));
                }
                return newId;
            }
            // 情境 B: 無 Key 或 Explicit Key (Guid/String) -> 直接 Insert
            else
            {
                await conn.ExecuteAsync(meta.InsertSql, entity, tx);
                return 0;
            }
        }

        public static async Task<int> BatchInsertAsync<T>(this IDbConnection conn, IEnumerable<T> entities, IDbTransaction? tx = null) where T : class
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            // 批次使用純 Insert SQL (不回傳 ID 以提升效能)
            return await conn.ExecuteAsync(meta.InsertSql, entities, tx);
        }

        public static async Task<bool> UpdateAsync<T>(this IDbConnection conn, T entity, IDbTransaction? tx = null) where T : class
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            if (string.IsNullOrEmpty(meta.UpdateSql))
                throw new InvalidOperationException($"Type {typeof(T).Name} has no Key defined or no updatable columns.");

            return await conn.ExecuteAsync(meta.UpdateSql, entity, tx) > 0;
        }

        public static async Task<bool> DeleteAsync<T, TKey>(this IDbConnection conn, TKey id, IDbTransaction? tx = null) where T : class
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            if (string.IsNullOrEmpty(meta.DeleteSql))
                throw new InvalidOperationException($"Type {typeof(T).Name} has no Key defined for Delete.");

            return await conn.ExecuteAsync(meta.DeleteSql, new { Id = id }, tx) > 0;
        }

        // ------------------------------------------------------------------------
        // Paged Query (Strategy Pattern)
        // ------------------------------------------------------------------------

        public static async Task<(List<T> Items, int Total)> QueryPagedListAsync<T>(
            this IDbConnection conn,
            string baseSql,
            object? param,
            int page,
            int pageSize,
            string orderBy)
        {
            if (page < 1) page = 1;
            int offset = (page - 1) * pageSize;

            var batchSql = Dialect.BuildPagingSql(baseSql, orderBy, offset, pageSize);

            using var multi = await conn.QueryMultipleAsync(batchSql, param);
            var items = (await multi.ReadAsync<T>()).ToList();
            var total = await multi.ReadFirstAsync<int>();

            return (items, total);
        }

        public static async Task<(IEnumerable<T> Items, int TotalCount)> QueryPagedAsync<T>(
            this IDbConnection conn,
            string sql,
            object? param,
            int page,
            int pageSize,
            string orderBy,
            IDbTransaction? tran = null)
        {
            if (page < 1) page = 1;
            int offset = (page - 1) * pageSize;

            string pagingSql = Dialect.BuildPagingSql(sql, orderBy, offset, pageSize);

            using var multi = await conn.QueryMultipleAsync(pagingSql, param, transaction: tran);
            var items = await multi.ReadAsync<T>();
            var total = await multi.ReadFirstAsync<int>();

            return (items, total);
        }
    }
}