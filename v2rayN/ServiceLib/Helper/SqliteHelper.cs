using System.Collections;
using SQLite;

namespace ServiceLib.Helper;

public sealed class SQLiteHelper
{
    private static readonly Lazy<SQLiteHelper> _instance = new(() => new());
    public static SQLiteHelper Instance => _instance.Value;
    private readonly string _connstr;
    private SQLiteConnection _db;
    private SQLiteAsyncConnection _dbAsync;
    private readonly string _configDB = "guiNDB.db";

    public class TableInfo
    {
        public int cid { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public int notnull { get; set; }
        public string dflt_value { get; set; }
        public int pk { get; set; }
    }

    public SQLiteHelper()
    {
        _connstr = Utils.GetConfigPath(_configDB);
        _db = new SQLiteConnection(_connstr, false);
        _dbAsync = new SQLiteAsyncConnection(_connstr, false);

        var columns = _db.Query<TableInfo>("PRAGMA table_info(ProfileGroupItem);");
        bool hasParentIndexId = columns.Any(c => c.name == "ParentIndexId");

        _db.RunInTransaction(() =>
        {
            if (hasParentIndexId)
            {
                _db.Execute("ALTER TABLE ProfileGroupItem RENAME COLUMN ParentIndexId TO IndexId;");
                _db.Execute(@"
                    UPDATE ProfileItem
                    SET configType = CASE configType
                        WHEN 100 THEN 201
                        WHEN 101 THEN 202
                        WHEN 102 THEN 203
                        WHEN 103 THEN 204
                        WHEN 104 THEN 205
                        WHEN 105 THEN 206
                        WHEN 1001 THEN 101
                        WHEN 1002 THEN 102
                        ELSE configType
                    END;
                ");
            }
        });
    }

    public CreateTableResult CreateTable<T>()
    {
        return _db.CreateTable<T>();
    }

    public async Task<int> InsertAllAsync(IEnumerable models)
    {
        return await _dbAsync.InsertAllAsync(models, runInTransaction: true).ConfigureAwait(false);
    }

    public async Task<int> InsertAsync(object model)
    {
        return await _dbAsync.InsertAsync(model);
    }

    public async Task<int> ReplaceAsync(object model)
    {
        return await _dbAsync.InsertOrReplaceAsync(model);
    }

    public async Task<int> UpdateAsync(object model)
    {
        return await _dbAsync.UpdateAsync(model);
    }

    public async Task<int> UpdateAllAsync(IEnumerable models)
    {
        return await _dbAsync.UpdateAllAsync(models, runInTransaction: true).ConfigureAwait(false);
    }

    public async Task<int> DeleteAsync(object model)
    {
        return await _dbAsync.DeleteAsync(model);
    }

    public async Task<int> DeleteAllAsync<T>()
    {
        return await _dbAsync.DeleteAllAsync<T>();
    }

    public async Task<int> ExecuteAsync(string sql)
    {
        return await _dbAsync.ExecuteAsync(sql);
    }

    public async Task<List<T>> QueryAsync<T>(string sql) where T : new()
    {
        return await _dbAsync.QueryAsync<T>(sql);
    }

    public AsyncTableQuery<T> TableAsync<T>() where T : new()
    {
        return _dbAsync.Table<T>();
    }

    public async Task DisposeDbConnectionAsync()
    {
        await Task.Factory.StartNew(() =>
        {
            _db?.Close();
            _db?.Dispose();
            _db = null;

            _dbAsync?.GetConnection()?.Close();
            _dbAsync?.GetConnection()?.Dispose();
            _dbAsync = null;
        });
    }
}
