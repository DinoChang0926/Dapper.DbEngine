using System.Data;
using System.Linq.Expressions;

namespace Dapper.DbEngine.Abstractions
{
    public interface IUpdateBuilder<T>
    {
        // 1. 設定要更新的欄位 (強型別鎖定)
        // 例如: .Set(x => x.Admin, true)
        IUpdateBuilder<T> Set<TProperty>(Expression<Func<T, TProperty>> property, TProperty value);

        // 2. 執行更新
        Task<int> ExecuteAsync(IDbTransaction? tran = null);
    }
}
