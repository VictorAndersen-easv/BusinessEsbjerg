using LinqToDB.Mapping;

namespace Infrastructure;

public class Account5Week
{
    [PrimaryKey] public string AccountID { get; set; }
    public string UserID { get; set; }
    public string SumTotalDays { get; set; }
    
    
}