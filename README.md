# Dapper.DbEngine

Dapper.DbEngine 是一個輕量級、高效能的 Dapper 擴充封裝庫。它旨在解決原生 Dapper 在 SQL 維護上的痛點，同時透過嚴謹的中介資料快取機制 (Metadata Caching)，保持接近手寫 SQL 的極致效能。

核心哲學：拒絕過度封裝，保持 SQL 的透明度，但在「組裝」與「映射」上追求零配置 (Zero-Allocation) 與絕對的一致性。

## ✨ 核心特性 (Features)

### 🚀 極致效能 (High Performance)

中介資料統一 (Metadata Unification)：全系統共用唯一的 TableMetadata，反射 (Reflection) 只在啟動時執行一次。

零記憶體配置 (Zero-Allocation)：高頻使用的 SQL 字串（如 INSERT, SELECT 列表）皆預先生成並快取，避免執行時期的 StringBuilder 開銷。

### 🛡️ 架構一致性 (Architectural Consistency)

統一入口：所有 SQL 建構邏輯收斂至 SqlBuilder，消除了 CRUD 與手寫查詢之間的欄位名稱不一致風險。

依賴反轉：透過 ISqlDialect 隔離資料庫差異 (SQL Server / PostgreSQL)，業務邏輯無需修改即可跨庫。

### 🔧 開發者體驗 (Developer Experience)

強型別支援：支援 Lambda 表達式解析 (x => x.UserName)，重構時欄位名稱自動連動。

簡潔的 CRUD：提供類似 Entity Framework 的 InsertAsync, UpdateAsync 等擴充方法，但底層仍是純粹的 Dapper。

## 🏗️ 專案架構 (Architecture)

Dapper.DbEngine
├── Abstractions           # [契約層] 定義核心介面 (IDbExecutor, ISqlDialect, IUpdateBuilder)
├── Builders               # [建構層] 統一的 SQL 生成入口 (SqlBuilder) 與 Fluent Update
├── Core                   # [核心層] Dapper 執行策略與基礎設施 (DbExecutor)
├── Dialects               # [策略層] 資料庫方言實作 (SqlServerDialect)
├── Extensions             # [擴充層] 對外公開的便利方法 (DapperExtensions)
├── Internals              # [底層機制] 封裝反射與快取細節 (TableMetadata, SqlMetadataCache)
└── Exceptions             # [異常層] 統一的資料庫操作異常定義


## 📦 安裝與配置 (Installation & Setup)

### 1. 定義實體 (Define Entities)

本專案支援標準的 System.ComponentModel.DataAnnotations 與 Dapper.Contrib 標籤。

using System.ComponentModel.DataAnnotations.Schema;
using Dapper.Contrib.Extensions; // 用於 ExplicitKey, Computed

[Table("Users", Schema = "dbo")] // 指定表名與 Schema
public class User
{
    [Key] // 自動遞增主鍵 (Identity)
    public int Id { get; set; }

    [ExplicitKey] // 或：手動指定主鍵 (如 Guid, String)
    public Guid Uid { get; set; }

    [Column("user_name")] // 映射 DB 欄位名
    public string Name { get; set; }

    public string Email { get; set; } // 預設映射為 [Email]

    [Computed] // 資料庫計算欄位 (Insert/Update 時跳過)
    public DateTime CreatedAt { get; set; }

    [Write(false)] // 唯讀欄位 (不參與寫入)
    public string FullDescription { get; set; }
}


### 2. 初始化方言 (Initialize Dialect)

在程式啟動時 (如 Program.cs) 配置全域方言。預設為 SqlServerDialect。

using Core.Infrastructure.Data.Extensions;

// 設定為 SQL Server (預設)
DapperExtensions.Dialect = new SqlServerDialect();

// 未來若支援 Postgres:
// DapperExtensions.Dialect = new PostgresDialect();


## 🛠️ 使用指南 (Usage Guide)

### 1. 基礎 CRUD 操作 (via DapperExtensions)

透過 IDbConnection 的擴充方法直接操作，享受強型別與自動 SQL 生成。

using Core.Infrastructure.Data.Extensions;

public async Task CreateUserAsync(User user)
{
    using var conn = _dbConnectionFactory.Create();

    // Insert: 自動處理 Identity 回填
    // 若為 ExplicitKey，則直接插入
    long newId = await conn.InsertAsync(user); 
}

public async Task UpdateUserAsync(User user)
{
    using var conn = _dbConnectionFactory.Create();

    // Update: 自動根據 [Key] 生成 WHERE 子句
    // 只更新非 Key 且可寫入的欄位
    await conn.UpdateAsync(user);
}

public async Task DeleteUserAsync(int id)
{
    using var conn = _dbConnectionFactory.Create();
    
    // Delete: 自動根據 [Key] 刪除
    await conn.DeleteAsync<User, int>(id);
}


### 2. 強型別查詢建構 (SqlBuilder)

當需要手寫複雜查詢 (WHERE, JOIN) 時，使用 SqlBuilder 確保欄位名稱與 CRUD 一致，避免 Magic String。

using Dapper.DbEngine.Builders;

public async Task<User?> GetUserByNameAsync(string name)
{
    // 1. 取得強型別欄位名 (自動加上方言引號，如 [user_name])
    // 支援 Lambda 解析，重構安全
    string colName = SqlBuilder.GetColName<User>(u => u.Name);
    
    // 2. 自動生成 SELECT 列表 (e.g. SELECT [Id], [user_name] AS [Name] ...)
    // 支援 Table Alias，避免欄位衝突
    string selectSql = SqlBuilder.BuildSelectSql<User>(tableAlias: "u");

    string sql = $"{selectSql} WHERE u.{colName} = @Name";

    return await _dbExecutor.QueryFirstOrDefaultAsync<User>(sql, new { Name = name });
}


### 3. Fluent Update Builder

針對「只更新部分欄位」的場景，避免取回整個實體再更新。

public async Task UpdateStatusAsync(int userId, int status)
{
    // 生成: UPDATE [Users] SET [Status] = @v_Status, [UpdatedAt] = @v_UpdatedAt WHERE [Id] = @w_Id
    await _baseRepository.CreateUpdateBuilder(userId)
        .Set(u => u.Status, status)
        .Set(u => u.UpdatedAt, DateTime.Now)
        .ExecuteAsync();
}


### 4. 分頁查詢 (Pagination)

使用 QueryPagedAsync 自動處理分頁 SQL (Offset/Fetch) 與總筆數統計。

public async Task<PagedList<User>> GetUsers(int page, int size)
{
    string baseSql = "SELECT * FROM [Users] WHERE [IsActive] = 1";
    
    // 自動生成分頁 SQL 並執行 (一次 Round-trip 取得 Items 與 Total)
    var (items, total) = await _dbExecutor.QueryPagedAsync<User>(
        baseSql, 
        param: null, 
        page: page, 
        pageSize: size, 
        orderBy: "CreatedAt DESC" // 必須提供排序
    );

    return new PagedList<User>(items, total, page, size);
}


### 5. 批次操作 (Batch Operations)

針對高效能場景，提供不回傳 Identity 的純批次執行。

var users = new List<User> { ... };

// 底層使用 Dapper 的 ExecuteAsync 進行批次參數綁定
// 效能遠高於迴圈 Insert
await conn.BatchInsertAsync(users);


## 🧩 擴充方言 (Extending Dialects)

若需支援新的資料庫 (如 PostgreSQL, MySQL)，只需實作 ISqlDialect 介面。

public class PostgresDialect : ISqlDialect
{
    public char OpenQuote => '"';
    public char CloseQuote => '"';
    public char ParameterPrefix => '@';
    public string BatchSeperator => ";";

    public string GetIdentitySql(string tableName, IEnumerable<string> cols, IEnumerable<string> params, string? idCol)
    {
        var sql = $"INSERT INTO {tableName} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", params)})";
        if (idCol != null) sql += $" RETURNING {idCol};"; // Postgres 風格
        return sql;
    }

    public string FormatDateColumn(string col) => $"NULLIF({col}, '')::timestamp";
    
    // ... 實作其他分頁邏輯
}


## ⚠️ 注意事項 (Notes)

Thread Safety: SqlMetadataCache 使用 ConcurrentDictionary，完全執行緒安全。

Warm-up: 首次存取某個 Type 時會觸發反射解析 (O(N))，後續呼叫均為 O(1) 快取讀取。

Keyless Entities: 若實體未定義 [Key]，則無法使用 UpdateAsync 與 DeleteAsync，但仍可使用 InsertAsync 與查詢功能。