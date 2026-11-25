using Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Service;

namespace api;

public class UserController(UserService service) : ControllerBase
{
    [HttpGet(nameof(GetUsers))]
    public List<User> GetUsers(int page, int resultsPerPage)
    {
        return service.GetUsers(page, resultsPerPage);
    }
}