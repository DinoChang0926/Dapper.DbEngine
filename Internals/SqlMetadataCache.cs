using Core.Infrastructure.Data.Extensions;
using System.Collections.Concurrent;

namespace Dapper.DbEngine.Internals
{
    public static class SqlMetadataCache
    {
        private static readonly ConcurrentDictionary<Type, TableMetadata> _cache = new();

        public static TableMetadata GetOrAdd(Type type)
        {
            // 這裡依賴全域 Dialect
            // 若要更嚴謹，可以讓 GetOrAdd 接收 dialect 參數，但通常 Dialect 是 App 生命週期唯一的
            return _cache.GetOrAdd(type, t => new TableMetadata(t, DapperExtensions.Dialect));
        }

        public static TableMetadata GetOrAdd<T>() => GetOrAdd(typeof(T));
    }
}
