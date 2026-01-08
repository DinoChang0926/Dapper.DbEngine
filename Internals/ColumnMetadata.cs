using System.Reflection;

namespace Dapper.DbEngine.Internals
{
    public record ColumnMetadata
    {
        public string PropName { get; init; } = null!;      // C# 屬性名稱 (e.g. "UserId")
        public string DbColName { get; init; } = null!;     // DB 欄位名稱 (e.g. "user_id")
        public string DbColQuoted { get; init; } = null!;   // 轉義後的名稱 (e.g. "[user_id]")
        public PropertyInfo PropertyInfo { get; init; } = null!;

        // 狀態標記
        public bool IsKey { get; init; }           // 是否為主鍵
        public bool IsExplicitKey { get; init; }   // 是否為手動輸入的主鍵 (非 Identity)
        public bool IsDateTime { get; init; }      // 是否為時間 (決定是否呼叫 Dialect 處理)
        public bool CanWrite { get; init; }        // 是否可寫入 (Update/Insert)
    }
}
