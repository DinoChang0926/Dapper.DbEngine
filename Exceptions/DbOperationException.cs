namespace Dapper.DbEngine.Exceptions
{
    public class DbOperationException : Exception
    {
        public string SqlStatement { get; }
        public long DurationMs { get; }

        public DbOperationException(string message, string sql, long durationMs, Exception innerException)
            : base(message, innerException)
        {
            SqlStatement = sql;
            DurationMs = durationMs;

            // 將關鍵資訊寫入 Data 字典，這樣即使上層只 Log Exception.ToString() 也能看到
            Data["SQL"] = sql;
            Data["DurationMs"] = durationMs;
        }
    }
}
