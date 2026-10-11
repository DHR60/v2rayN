using System.Linq.Expressions;

namespace ServiceLib.Helper;

public sealed class SQLiteHelper
{
    private static readonly Lazy<SQLiteHelper> _instance = new(() => new());
    public static SQLiteHelper Instance => _instance.Value;
    private readonly string _connstr;
    private SQLiteDatabase _db;
    private readonly string _configDB = "guiNDB.db";

    public SQLiteHelper()
    {
        _connstr = Utils.GetConfigPath(_configDB);
        var options = new SQLiteOptionsBuilder(_connstr)
            .UseGeneratedMaterializers()
            .Build();
        _db = new SQLiteDatabase(options);
    }

    public int CreateTable<T>()
    {
        return _db.Schema.CreateTable<T>();
    }

    public Task<int> CreateTableAsync<T>()
    {
        return _db.Schema.CreateTableAsync<T>();
    }

    public Task<int> InsertAllAsync<T>(IEnumerable<T> models)
    {
        return _db.Table<T>().AddRangeAsync(models, runInTransaction: true);
    }

    public Task<int> InsertAsync<T>(T model)
    {
        return _db.Table<T>().AddAsync(model);
    }

    public Task<int> ReplaceAsync<T>(T model)
    {
        return _db.Table<T>().AddOrUpdateAsync(model);
    }

    public Task<int> UpdateAsync<T>(T model)
    {
        return _db.Table<T>().UpdateAsync(model);
    }

    public Task<int> UpdateAllAsync<T>(IEnumerable<T> models)
    {
        return _db.Table<T>().UpdateRangeAsync(models, runInTransaction: true);
    }

    public Task<int> DeleteAsync<T>(T model)
    {
        return _db.Table<T>().RemoveAsync(model);
    }

    public Task<int> DeleteAllAsync<T>()
    {
        return _db.Table<T>().ClearAsync();
        // return _db.Schema.DropTableAsync<T>();
    }

    //public Task<int> ExecuteAsync(string sql)
    //{
    //    return _db.ExecuteAsync(sql);
    //}

    //public Task<List<T>> QueryAsync<T>(string sql) where T : new()
    //{
    //    return _db.QueryAsync<T>(sql);
    //}

    public Task<T?> FirstOrDefaultAsync<T>()
    {
        return _db.Table<T>().FirstOrDefaultAsync();
    }

    public Task<T?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>> predicate)
    {
        return _db.Table<T>().FirstOrDefaultAsync(predicate);
    }

    public Task<List<T>> FetchAllAsync<T>()
    {
        return _db.Table<T>().ToListAsync();
    }

    public Task<List<T>> FetchAsync<T>(Expression<Func<T, bool>> predicate)
    {
        return _db.Table<T>().Where(predicate).ToListAsync();
    }

    public Task<List<T>> FetchPagedAsync<T>(
        Expression<Func<T, bool>> predicate,
        int? take = null,
        int? offset = null)
    {
        var query = _db.Table<T>().Where(predicate);
        if (offset.HasValue)
        {
            query = query.Skip(offset.Value);
        }
        if (take.HasValue)
        {
            query = query.Take(take.Value);
        }
        return query.ToListAsync();
    }

    public Task<int> DeleteWhereAsync<T>(Expression<Func<T, bool>> predicate)
    {
        return _db.Table<T>()
            .Where(predicate)
            .ExecuteDeleteAsync();
    }

    public Task<int> DeleteOrphanServerStatsAsync()
    {
        const string sql = @"
        DELETE FROM ServerStatItem 
        WHERE IndexId NOT IN (SELECT IndexId FROM ProfileItem)";

        return _db.ExecuteAsync(sql, Array.Empty<object>());
    }

    public Task<int> CountAsync<T>()
    {
        return _db.Table<T>().CountAsync();
    }

    public Task<int> CountAsync<T>(Expression<Func<T, bool>> predicate)
    {
        return _db.Table<T>().Where(predicate).CountAsync();
    }

    public SQLiteTable<T> Table<T>()
    {
        return _db.Table<T>();
    }

    public async Task DisposeDbConnectionAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                //_db?.Close();
                _db?.Dispose();
            }
            finally
            {
                _db = null;
            }
        });
    }
}
