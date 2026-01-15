using PersonalFinanceApi.Dtos;
using PersonalFinanceApi.Services;

var builder = WebApplication.CreateBuilder(args);

// DI: сервис один на приложение
builder.Services.AddSingleton<TransactionService>();

var app = builder.Build();

app.MapGet("/", () => "Personal Finance API is running");

// проверка подключения к БД
app.MapGet("/db-test", async (TransactionService s) => await s.DbTestAsync());

// CRUD
app.MapPost("/transactions", async (CreateTransactionDto dto, TransactionService s) =>
{
    try
    {
        var created = await s.CreateAsync(dto);
        return Results.Created($"/transactions/{created.Id}", created);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapGet("/transactions", async (string? kind, string? category, TransactionService s) =>
{
    try
    {
        return Results.Ok(await s.GetAllAsync(kind, category));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapGet("/transactions/{id:int}", async (int id, TransactionService s) =>
{
    var tx = await s.GetByIdAsync(id);
    return tx is null ? Results.NotFound() : Results.Ok(tx);
});

app.MapPut("/transactions/{id:int}", async (int id, UpdateTransactionDto dto, TransactionService s) =>
{
    try
    {
        var updated = await s.UpdateAsync(id, dto);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapDelete("/transactions/{id:int}", async (int id, TransactionService s) =>
{
    var ok = await s.DeleteAsync(id);
    return ok ? Results.NoContent() : Results.NotFound();
});

// balance
app.MapGet("/balance", async (TransactionService s) => Results.Ok(await s.GetBalanceAsync()));

app.Urls.Add("http://localhost:5000");
app.Run();
