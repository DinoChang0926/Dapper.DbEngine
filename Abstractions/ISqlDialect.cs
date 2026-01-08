namespace Dapper.DbEngine.Abstractions
{
    public interface ISqlDialect
    {
        // 基礎符號定義
        char OpenQuote { get; }
        char CloseQuote { get; }
        string BatchSeperator { get; }
        char ParameterPrefix { get; }

        /// <summary>
        /// 建構 Insert 語句並處理 Identity 回傳 (解決 RETURNING/SCOPE_IDENTITY 差異)
        /// </summary>
        string GetIdentitySql(string tableName, IEnumerable<string> columns, IEnumerable<string> paramNames, string? identityColumn);

        /// <summary>
        /// 生成分頁與總數查詢的批次 SQL
        /// </summary>
        string BuildPagingSql(string baseSql, string orderBy, int offset, int limit);

        string FormatDateColumn(string columnName);
    }
}
