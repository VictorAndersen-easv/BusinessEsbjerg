using Infrastructure.Entities;
using LinqToDB;
using LinqToDB.Data;

namespace Infrastructure;

/// <summary>
/// Our connection to the database. One instance is created per web request
/// and closed when the request ends. Each ITable property is a table we can query with LINQ.
/// </summary>
public class ExamDatabase : DataConnection
{
    public ExamDatabase(DataOptions options) : base(options) { }

    public ITable<Employee> Employees => this.GetTable<Employee>();
}