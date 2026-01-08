using Dapper.DbEngine.Abstractions;

namespace Dapper.DbEngine.Dialects
{
    // ========================================================================
    // 1. SQL 方言介面與實作 (Dialect Strategy)
    // ======================================================================== 

    public class SqlServerDialect : ISqlDialect
    {
        public char OpenQuote => '[';
        public char CloseQuote => ']';
        public char ParameterPrefix => '@';
        public string BatchSeperator => ";";

        public string GetIdentitySql(string tableName, IEnumerable<string> columns, IEnumerable<string> paramNames, string? identityColumn)
        {
            var sql = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramNames)});";

            if (!string.IsNullOrEmpty(identityColumn))
            {
                // SQL Server: 附加 SELECT SCOPE_IDENTITY()
                sql += " SELECT CAST(SCOPE_IDENTITY() AS bigint);";
            }
            return sql;
        }

        public string BuildPagingSql(string baseSql, string orderBy, int offset, int limit)
        {
            // SQL Server 的 OFFSET FETCH 強制需要 ORDER BY
            if (string.IsNullOrWhiteSpace(orderBy))
            {
                throw new ArgumentException("SQL Server pagination requires an ORDER BY clause.", nameof(orderBy));
            }

            baseSql = baseSql.Trim().TrimEnd(';');

            // 組合 Batch SQL:
            // 1. 資料查詢: 加上 ORDER BY 與 OFFSET
            // 2. 總數查詢: 包裹子查詢 (Count 不受 Order By 影響)
            var batchSql = $@"
                {baseSql}
                {orderBy}
                OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY;
                
                SELECT COUNT(1) FROM ({baseSql}) AS [CountTable];
            ";

            return batchSql;
        }

        public string FormatDateColumn(string columnName)
        {
            // SQL Server 特化：處理空字串轉 NULL 並嘗試轉換
            return $"TRY_CONVERT(datetime2, NULLIF({columnName}, ''))";
        }
    }
}
