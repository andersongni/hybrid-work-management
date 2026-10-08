using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PresenceTracker.Infrastructure;
using PresenceTracker.Persistence;

namespace PresenceTracker.Tests;

internal sealed class SqliteTestHarness : IAsyncDisposable
{
    private SqliteTestHarness(SqliteConnection connection, TrackerDbContext db, EfTrackerRepository repository)
    {
        Connection = connection;
        Db = db;
        Repository = repository;
    }

    public SqliteConnection Connection { get; }
    public TrackerDbContext Db { get; }
    public EfTrackerRepository Repository { get; }

    public static async Task<SqliteTestHarness> CreateAsync(bool initialize = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TrackerDbContext>().UseSqlite(connection).Options;
        var db = new TrackerDbContext(options);
        var repository = new EfTrackerRepository(db, new LocalHolidayProvider());
        if (initialize)
            await repository.InitializeAsync();
        return new SqliteTestHarness(connection, db, repository);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
