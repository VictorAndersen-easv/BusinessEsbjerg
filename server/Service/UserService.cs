using System.ComponentModel.DataAnnotations;
using Infrastructure;
using LinqToDB;

namespace Service;

public class UserService(MyDatabaseConnection db)
{
    public List<User> GetUsers()
    {
       
        
        return db.Users.ToList();
    }

    public List<User> GetUsers(int page, int resultsPerPage)
    {
        if (page < 1)
            throw new ValidationException("Page must be 1 or higher");
        if (resultsPerPage < 1)
            throw new ValidationException("Must have 1 or more result per page");
        
        return db.Users
            .LoadWith(u => u.Account5)
            .LoadWith(u => u.Account6)
            .Take(resultsPerPage)
            .Skip((page - 1) * resultsPerPage)
            .ToList();
    }
}