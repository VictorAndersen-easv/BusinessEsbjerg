using LinqToDB.Mapping;

namespace Infrastructure;

public class User
{
    [PrimaryKey] public string UserID { get; set; }
    public string UserName { get; set; }
    public string UserEmail { get; set; }
    public string UserPassword { get; set; }
    
    
    [Association(ThisKey = nameof(Account5ID), OtherKey = nameof(Account5Week.AccountID))]
    public string Account5ID { get; set; }
    [Association(ThisKey = nameof(Account6ID), OtherKey = nameof(Account6Week.AccountID))]
    public string Account6ID { get; set; }
    public Account5Week Account5 { get; set; }
    public Account6Week Account6 { get; set; }

}
