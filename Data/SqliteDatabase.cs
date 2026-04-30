namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }
}

