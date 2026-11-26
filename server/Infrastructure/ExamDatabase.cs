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
    public ITable<UserAccount> UserAccounts => this.GetTable<UserAccount>();
    public ITable<WorkSchedule> WorkSchedules => this.GetTable<WorkSchedule>();
    public ITable<WorkScheduleDay> WorkScheduleDays => this.GetTable<WorkScheduleDay>();
    public ITable<CalendarDay> CalendarDays => this.GetTable<CalendarDay>();
    public ITable<HolidayAccountType> HolidayAccountTypes => this.GetTable<HolidayAccountType>();
    public ITable<HolidayBucket> HolidayBuckets => this.GetTable<HolidayBucket>();
    public ITable<HolidayRequest> HolidayRequests => this.GetTable<HolidayRequest>();
    public ITable<HolidayRequestDay> HolidayRequestDays => this.GetTable<HolidayRequestDay>();
    public ITable<LedgerEntry> LedgerEntries => this.GetTable<LedgerEntry>();
    public ITable<RuleParameter> RuleParameters => this.GetTable<RuleParameter>();
}