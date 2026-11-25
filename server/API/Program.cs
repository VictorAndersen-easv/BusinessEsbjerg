using Infrastructure;
using LinqToDB;
using Service;

var builder = WebApplication.CreateBuilder(args);

//Database stuff
var options = new DataOptions<MyDatabaseConnection>(
    new DataOptions().UseSQLite("Data Source=db.db"));
builder.Services.AddScoped<MyDatabaseConnection>(_ => new MyDatabaseConnection(options));

builder.Services.AddScoped<UserService>();
builder.Services.AddControllers();
builder.Services.AddCors();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<MyExceptionHandler>();

//Need this for swagger
builder.Services.AddOpenApiDocument();

//No clue what this really does
var app = builder.Build();




using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MyDatabaseConnection>();
    db.CreateTable<User>(tableOptions:TableOptions.CreateIfNotExists);
    if (db.Users.Count() == 0)
    {
        db.Insert(new User()
        {
            UserID = "123321",
            UserEmail = "fuckmail",
            UserName = "fuckname",
            UserPassword = "fuckword"
        });
    }
    
}

app.UseExceptionHandler();
app.UseCors(config => config.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().SetIsOriginAllowed(_ => true));
app.MapControllers();

app.UseOpenApi();
app.UseSwaggerUi();

app.Run();