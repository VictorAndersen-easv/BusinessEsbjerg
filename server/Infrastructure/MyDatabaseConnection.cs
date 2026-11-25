using LinqToDB;
using LinqToDB.Data;

namespace Infrastructure;

public class MyDatabaseConnection : DataConnection
{
    public MyDatabaseConnection(DataOptions<MyDatabaseConnection> options) : base(options.Options)
    {
    }

    public ITable<User> Users => this.GetTable<User>();
}