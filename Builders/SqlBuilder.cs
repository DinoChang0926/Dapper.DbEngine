using Core.Infrastructure.Data.Extensions; // 引用 Dialect
using Dapper.DbEngine.Internals;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Dapper.DbEngine.Builders
{
    /// <summary>
    /// [統一入口] SQL 建構與輔助核心。
    /// <para>整合了 SELECT 生成、Table 名稱獲取、Lambda 欄位解析。</para>
    /// </summary>
    public static class SqlBuilder
    {
        // ========================================================================
        // 1. [原 SqlHelper] 基礎資訊獲取 (Table & Column)
        // ========================================================================

        /// <summary>
        /// 取得實體對應的完整資料庫表名 (包含 Schema 與括號，e.g. "[dbo].[User]")
        /// </summary>
        public static string GetTableName<T>()
        {
            return SqlMetadataCache.GetOrAdd<T>().FormattedTableName;
        }

        public static string GetTableName(Type type)
        {
            return SqlMetadataCache.GetOrAdd(type).FormattedTableName;
        }

        /// <summary>
        /// 解析 Lambda 取得欄位名 (e.g. u => u.Name -> "[user_name]")
        /// </summary>
        public static string GetColName<T>(Expression<Func<T, object>> selector)
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            var propInfo = ResolveProperty(selector);

            // 線性搜尋 (效能極快)
            var col = meta.AllColumns.FirstOrDefault(c => c.PropertyInfo == propInfo);

            if (col == null)
            {
                throw new InvalidOperationException($"Property '{propInfo.Name}' is not mapped to a valid SQL column in type '{typeof(T).Name}'.");
            }

            return col.DbColQuoted;
        }

        public static string GetColName<T>(string propName)
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            // 線性搜尋
            var col = meta.AllColumns.FirstOrDefault(c => c.PropName.Equals(propName, StringComparison.OrdinalIgnoreCase));
            if (col == null) throw new ArgumentException($"Property '{propName}' not found on type {typeof(T).Name}");
            return col.DbColQuoted;
        }

        // ========================================================================
        // 2. [原 SqlSelectBuilder] 批量 SELECT 生成
        // ========================================================================

        public record ColumnSchema(string SelectionSql, string QuotedAlias, string QuotedPropName);

        public static IEnumerable<ColumnSchema> GetColumnSchemas<T>(string tableAlias, string aliasPrefix)
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();

            foreach (var col in meta.AllColumns)
            {
                var rawAliasName = aliasPrefix + col.PropName;
                var quotedAlias = SqlSyntax.Escape(rawAliasName);
                var propNameQuoted = SqlSyntax.Escape(col.PropName);

                string sourceCol = SqlSyntax.ApplyAlias(col.DbColQuoted, tableAlias);
                var selection = $"{sourceCol} AS {quotedAlias}";

                yield return new ColumnSchema(selection, quotedAlias, propNameQuoted);
            }
        }

        public static string GetColumns<T>(string? tableAlias = null, string? columnPrefix = null)
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            return BuildColumnString(meta.AllColumns, tableAlias, columnPrefix);
        }

        public static IEnumerable<string> GetColumnList<T>(string? tableAlias = null, string? columnPrefix = null)
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            return BuildColumnEnumerable(meta.AllColumns, tableAlias, columnPrefix);
        }

        public static string BuildSelectSql<T>(string? tableAlias = null, string? whereClause = null)
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            var sb = new StringBuilder("SELECT ");

            // 1. Columns
            sb.Append(BuildColumnString(meta.AllColumns, tableAlias, null));

            // 2. From (直接複用內部的 GetTableName 邏輯)
            sb.Append(" FROM ").Append(meta.FormattedTableName);

            // 3. Table Alias
            if (!string.IsNullOrWhiteSpace(tableAlias))
            {
                sb.Append(' ').Append(SqlSyntax.Escape(tableAlias));
            }

            // 4. Where
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                if (!whereClause.TrimStart().StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
                    sb.Append(" WHERE ");
                sb.Append(whereClause);
            }

            return sb.ToString();
        }

        public static string GetKeyColName<T>()
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            if (meta.KeyColumn == null) throw new InvalidOperationException($"Type {typeof(T).Name} has no Key.");
            return meta.KeyColumn.DbColQuoted;
        }



        public static string GetInsertSql<T>(bool returnId)
        {
            var meta = SqlMetadataCache.GetOrAdd<T>();
            return returnId ? meta.InsertSqlAndReturnId : meta.InsertSql;
        }

        public static string? GetUpdateSql<T>() => SqlMetadataCache.GetOrAdd<T>().UpdateSql;

        public static string? GetDeleteSql<T>() => SqlMetadataCache.GetOrAdd<T>().DeleteSql;


        // ========================================================================
        // 3. Private Helpers (內部共用邏輯)
        // ========================================================================

        private static string BuildColumnString(ColumnMetadata[] columns, string? alias, string? prefix)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var col in columns)
            {
                if (!first) sb.Append(",\n");
                first = false;

                // 核心 formatting 邏輯
                AppendColumnSql(sb, col, alias, prefix);
            }
            return sb.ToString();
        }

        private static IEnumerable<string> BuildColumnEnumerable(ColumnMetadata[] columns, string? alias, string? prefix)
        {
            foreach (var col in columns)
            {
                var sb = new StringBuilder();
                AppendColumnSql(sb, col, alias, prefix);
                yield return sb.ToString();
            }
        }

        // [重構提取] 將單一欄位的 SQL 拼接邏輯提取出來，確保一致性
        private static void AppendColumnSql(StringBuilder sb, ColumnMetadata col, string? alias, string? prefix)
        {
            string scopedCol = SqlSyntax.ApplyAlias(col.DbColQuoted, alias ?? string.Empty);

            if (col.IsDateTime)
                sb.Append(DapperExtensions.Dialect.FormatDateColumn(scopedCol));
            else
                sb.Append(scopedCol);

            sb.Append(" AS [");
            if (!string.IsNullOrEmpty(prefix)) sb.Append(prefix);
            sb.Append(col.PropName).Append(']');
        }

        private static PropertyInfo ResolveProperty(LambdaExpression expression)
        {
            Expression body = expression.Body;

            if (body is UnaryExpression unary)
            {
                body = unary.Operand;
            }

            if (body is MemberExpression member && member.Member is PropertyInfo prop)
            {
                return prop;
            }

            throw new ArgumentException($"Expression '{expression}' does not refer to a property.");
        }
    }
}