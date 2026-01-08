using Dapper.DbEngine.Abstractions;
using Dapper.DbEngine.Internals;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace Dapper.DbEngine.Builders
{
    public class UpdateBuilder<T, TKey> : IUpdateBuilder<T> where T : class
    {
        private readonly IDbExecutor _dbExecutor;
        private readonly TKey _id;
        private readonly string _tableName;

        // Key: DB欄位名, Value: 參數值
        private readonly Dictionary<string, object?> _updates = new();

        public UpdateBuilder(IDbExecutor dbExecutor, TKey id, string? tableName = null)
        {
            _dbExecutor = dbExecutor ?? throw new ArgumentNullException(nameof(dbExecutor));
            _id = id;
            _tableName = tableName ?? SqlBuilder.GetTableName<T>();
        }

        public IUpdateBuilder<T> Set<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
        {
            // 1. 解析 PropertyInfo (使用優化後的邏輯)
            var propInfo = GetPropertyInfo(property);

            // 2. 獲取 DB 欄位名稱
            string dbColName = SqlBuilder.GetColName<T>(propInfo.Name);

            // 3. 存入字典 (允許 value 為 null，視 DB Schema 而定)
            _updates[dbColName] = value;
            return this;
        }

        public async Task<int> ExecuteAsync(IDbTransaction? tran = null)
        {
            // 若無欄位需更新，直接返回 0，避免無效 SQL
            if (_updates.Count == 0) return 0;

            var parameters = new DynamicParameters();
            var setClauses = new List<string>(_updates.Count);

            // 1. 建構 SET 子句與參數 (使用 v_ 前綴避免衝突)
            foreach (var kvp in _updates)
            {
                // 防禦性編碼：參數名稱不應依賴外部輸入，強制加上前綴
                string paramName = $"v_{kvp.Key}";

                // SQL: [ColName] = @v_ColName
                setClauses.Add($"{SqlSyntax.Escape(kvp.Key)} = @{paramName}");

                parameters.Add(paramName, kvp.Value);
            }

            // 2. 處理 WHERE 子句 (使用 w_ 前綴避免與 SET 中的 Id 欄位衝突)
            string pkName = SqlBuilder.GetKeyColName<T>();
            string pkParamName = "w_Id";
            parameters.Add(pkParamName, _id);

            // 3. 組裝 SQL (使用 StringBuilder 或 String Interpolation)
            // UPDATE [Table] SET [Col1]=@v_Col1, [Col2]=@v_Col2 WHERE [PK]=@w_Id
            string sql = $"UPDATE {_tableName} SET {string.Join(", ", setClauses)} WHERE {SqlSyntax.Escape(pkName)} = @{pkParamName}";

            // 4. 執行
            return await _dbExecutor.ExecuteSqlAsync(sql, parameters, tran);
        }

        /// <summary>
        /// 從 Expression 解析 PropertyInfo，支援 Pattern Matching 與 Boxing 拆箱
        /// </summary>
        private static PropertyInfo GetPropertyInfo<TSource, TProp>(Expression<Func<TSource, TProp>> propertyLambda)
        {
            Expression body = propertyLambda.Body;

            // 處理 Boxing (例如 x => (object)x.Id)
            if (body is UnaryExpression unary)
            {
                body = unary.Operand;
            }

            // 檢查是否為 MemberExpression
            if (body is not MemberExpression member)
            {
                throw new ArgumentException($"Expression '{propertyLambda}' refers to a method, not a property.");
            }

            // 檢查是否為 PropertyInfo
            if (member.Member is not PropertyInfo propInfo)
            {
                throw new ArgumentException($"Expression '{propertyLambda}' refers to a field, not a property.");
            }

            // 驗證屬性是否屬於該型別 (防止閉包引用錯誤)
            // 注意：這裡只檢查 ReflectedType，若有繼承結構需求可放寬限制
            if (typeof(TSource) != propInfo.ReflectedType && !typeof(TSource).IsSubclassOf(propInfo.ReflectedType!))
            {
                throw new ArgumentException($"Expression '{propertyLambda}' refers to a property that is not from type {typeof(TSource)}.");
            }

            return propInfo;
        }
    }
}