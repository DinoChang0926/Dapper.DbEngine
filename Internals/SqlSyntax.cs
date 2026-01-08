// 建議 Namespace：與 SqlSelectBuilder 保持相近，或放在 Infrastructure 核心
namespace Dapper.DbEngine.Internals
{
    /// <summary>
    /// [Infrastructure Level] SQL 語法標準化引擎。
    /// <para>職責：全系統統一的識別字轉義 (Escaping) 與 Alias 注入規則。</para>
    /// </summary>
    public static class SqlSyntax // 修改為 public
    {
        // 定義非法字元：用於檢查 "純識別字" (Identifier)
        // 包含 '.' (Alias), '(' (Function), ')' (Function), ']' (Injection Closing)
        private static readonly char[] _forbiddenChars = { '.', '(', ')', ']' };

        /// <summary>
        /// [嚴格模式] 轉義識別字。
        /// <para>輸入: "Name" -> 輸出: "[Name]"</para>
        /// <para>安全性: 若輸入包含非法字元 (如 SQL Injection 向量)，立即拋出異常 (Fail Fast)。</para>
        /// </summary>
        public static string Escape(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return "";

            // 使用 Span 進行零配置 (Zero Allocation) 的字元檢查
            var span = identifier.AsSpan().Trim();

            // Fail Fast: 發現非法字元立即停止
            if (span.IndexOfAny(_forbiddenChars) >= 0)
            {
                throw new ArgumentException($"Invalid identifier: '{identifier}'. Aliases or Expressions are not allowed in raw identifiers.");
            }

            // 冪等性處理 (Idempotency)
            span = span.TrimStart('[').TrimEnd(']');

            return $"[{span.ToString()}]";
        }

        /// <summary>
        /// [智慧注入] 根據上下文決定是否套用 Table Alias。
        /// <para>若欄位為單純識別字，則回傳 "{alias}.{column}"，否則回傳原值。</para>
        /// </summary>
        public static string ApplyAlias(string column, string alias)
        {
            if (string.IsNullOrWhiteSpace(column)) return "";
            if (string.IsNullOrWhiteSpace(alias)) return column;

            if (IsSimpleIdentifier(column))
            {
                return $"{alias}.{column}";
            }

            return column;
        }

        /// <summary>
        /// 判斷字串是否為單純欄位 (無函數、無運算子、無 Alias)
        /// </summary>
        public static bool IsSimpleIdentifier(string col)
        {
            if (string.IsNullOrWhiteSpace(col)) return false;
            // 檢查是否包含 . (已含 alias) 或 ( (函數)
            return col.AsSpan().IndexOfAny('.', '(') < 0;
        }
    }
}