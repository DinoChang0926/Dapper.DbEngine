using Dapper.DbEngine.Abstractions;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using ColumnAttribute = System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
using DapperComputedAttribute = Dapper.Contrib.Extensions.ComputedAttribute;
using DapperExplicitKeyAttribute = Dapper.Contrib.Extensions.ExplicitKeyAttribute;
using DapperKeyAttribute = Dapper.Contrib.Extensions.KeyAttribute;
using DapperTableAttribute = Dapper.Contrib.Extensions.TableAttribute;
using DapperWriteAttribute = Dapper.Contrib.Extensions.WriteAttribute;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Dapper.DbEngine.Internals
{
    /// <summary>
    /// [核心] 統一的 Type 中介資料快取。
    /// 負責一次性解析 Attribute 並生成 CRUD SQL，避免重複 Reflection。
    /// </summary>
    public sealed class TableMetadata
    {
        // 基本資訊
        public string FormattedTableName { get; }
        public ColumnMetadata? KeyColumn { get; }
        public ColumnMetadata[] AllColumns { get; }

        // 預生成的 CRUD SQL (Lazy Loading 或建構時生成皆可，這裡採建構時生成以換取 Runtime 效能)
        public string InsertSql { get; }
        public string InsertSqlAndReturnId { get; }
        public string? UpdateSql { get; }
        public string? DeleteSql { get; }

        // 建構子：傳入 Type 與 Dialect 進行解析
        public TableMetadata(Type type, ISqlDialect dialect)
        {
            // ---------------------------------------------------------
            // 1. 解析表格名稱 (邏輯合併自 SqlSelectBuilder)
            // ---------------------------------------------------------
            var tableAttr = type.GetCustomAttribute<TableAttribute>();
            var dapperTableAttr = type.GetCustomAttribute<DapperTableAttribute>();

            string rawName = tableAttr?.Name ?? dapperTableAttr?.Name ?? type.Name;
            string? explicitSchema = tableAttr?.Schema;

            // 處理 "dbo.User" 這種寫法
            if (string.IsNullOrEmpty(explicitSchema) && rawName.Contains('.'))
            {
                var parts = rawName.Split('.');
                if (parts.Length == 2) { explicitSchema = parts[0]; rawName = parts[1]; }
            }

            string Clean(string s) => s.Trim().Replace("\"", "").Replace("`", "").Replace("[", "").Replace("]", "");

            var qOpen = dialect.OpenQuote;
            var qClose = dialect.CloseQuote;

            // 格式化表名: [Schema].[Table]
            FormattedTableName = !string.IsNullOrEmpty(explicitSchema)
                ? $"{qOpen}{Clean(explicitSchema)}{qClose}.{qOpen}{Clean(rawName)}{qClose}"
                : $"{qOpen}{Clean(rawName)}{qClose}";

            // ---------------------------------------------------------
            // 2. 解析欄位 (邏輯合併自 DapperExtensions)
            // ---------------------------------------------------------
            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                            .Where(p => p.CanRead);

            var colList = new List<ColumnMetadata>();

            foreach (var p in props)
            {
                // 排除邏輯
                if (p.IsDefined(typeof(NotMappedAttribute)) ||
                    p.IsDefined(typeof(DapperComputedAttribute)))
                {
                    continue;
                }

                var writeAttr = p.GetCustomAttribute<DapperWriteAttribute>();
                bool canWrite = writeAttr?.Write ?? true; // 預設可寫

                // 解析名稱
                var colAttr = p.GetCustomAttribute<ColumnAttribute>();
                string dbName = colAttr?.Name ?? p.Name;
                string dbQuoted = SqlSyntax.Escape(dbName); // 使用你的 SqlSyntax

                // 判斷 Key
                bool isExplicitKey = p.IsDefined(typeof(DapperExplicitKeyAttribute));
                bool isKey = isExplicitKey ||
                             p.IsDefined(typeof(DapperKeyAttribute)) ||
                             p.IsDefined(typeof(System.ComponentModel.DataAnnotations.KeyAttribute)) ||
                             p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase);

                // 智慧判斷 Explicit (String/Guid 視為 Explicit)
                if (isKey && !isExplicitKey)
                {
                    var typeCode = Type.GetTypeCode(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType);
                    if (typeCode == TypeCode.String || typeCode == TypeCode.Object)
                        isExplicitKey = true;
                }

                // 判斷 DateTime
                bool isDate = p.PropertyType == typeof(DateTime) ||
                              p.PropertyType == typeof(DateTime?) ||
                              Nullable.GetUnderlyingType(p.PropertyType) == typeof(DateTime);

                colList.Add(new ColumnMetadata
                {
                    PropName = p.Name,
                    DbColName = dbName,
                    DbColQuoted = dbQuoted,
                    PropertyInfo = p,
                    IsKey = isKey,
                    IsExplicitKey = isExplicitKey,
                    CanWrite = canWrite,
                    IsDateTime = isDate
                });
            }

            AllColumns = colList.ToArray();
            KeyColumn = AllColumns.FirstOrDefault(c => c.IsKey);

            // ---------------------------------------------------------
            // 3. 生成 CRUD SQL (邏輯移轉自 DapperExtensions)
            // ---------------------------------------------------------

            // INSERT
            // 排除 Identity Key (除非是 Explicit) 且必須 CanWrite
            var insertCols = AllColumns.Where(c => (c.IsExplicitKey || !c.IsKey) && c.CanWrite).ToList();

            var colNames = insertCols.Select(c => c.DbColQuoted);
            var paramNames = insertCols.Select(c => $"{dialect.ParameterPrefix}{c.PropName}");

            // 一般 Insert
            InsertSql = dialect.GetIdentitySql(FormattedTableName, colNames, paramNames, null);

            // 回傳 ID 的 Insert
            if (KeyColumn != null && !KeyColumn.IsExplicitKey)
            {
                InsertSqlAndReturnId = dialect.GetIdentitySql(FormattedTableName, colNames, paramNames, KeyColumn.DbColName);
            }
            else
            {
                InsertSqlAndReturnId = InsertSql; // Fallback
            }

            // UPDATE
            if (KeyColumn != null)
            {
                var updateCols = AllColumns.Where(c => !c.IsKey && c.CanWrite).ToList(); // 加上 ToList 以便檢查數量

                if (updateCols.Any())
                {
                    var setClause = string.Join(", ", updateCols.Select(c => $"{c.DbColQuoted} = {dialect.ParameterPrefix}{c.PropName}"));
                    UpdateSql = $"UPDATE {FormattedTableName} SET {setClause} WHERE {KeyColumn.DbColQuoted} = {dialect.ParameterPrefix}{KeyColumn.PropName}";
                }
                else
                {
                    UpdateSql = null;
                }
            }

            // DELETE
            if (KeyColumn != null)
            {
                DeleteSql = $"DELETE FROM {FormattedTableName} WHERE {KeyColumn.DbColQuoted} = {dialect.ParameterPrefix}Id";
                // 注意：DeleteAsync 習慣傳入 new { Id = id }，所以參數名固定為 @Id
            }
        }
    }
}
