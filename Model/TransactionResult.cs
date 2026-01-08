namespace Dapper.DbEngine.Model
{
    public enum TransactionStatus
    {
        Success,
        DuplicateKey, // 對應 SQL 2627/2601
        SystemError,
        NotFound
    }
    public class TransactionResult
    {
        public TransactionStatus Status { get; set; }
        public string? Message { get; set; }
        public bool IsSuccess => Status == TransactionStatus.Success;
        public static TransactionResult Ok() => new() { Status = TransactionStatus.Success };
        public static TransactionResult Conflict(string msg) => new() { Status = TransactionStatus.DuplicateKey, Message = msg };
        public static TransactionResult Error(string msg, TransactionStatus status = TransactionStatus.SystemError) => new() { Status = status, Message = msg };
    }
}
