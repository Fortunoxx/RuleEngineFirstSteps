using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapPost("/transactions", (TransactionDto transaction) =>
{
    // Process the transaction (in a real app, you'd save to database)
    var result = new
    {
        Id = Guid.NewGuid(),
        Text = transaction.Text,
        Amount = transaction.Amount,
        Date = transaction.Date,
        ProcessedAt = DateTime.UtcNow,
        Status = "Processed"
    };
    
    return Results.Created($"/transactions/{result.Id}", result);
})
.WithName("CreateTransaction")
.WithSummary("Create a new transaction")
.WithDescription("Creates a new transaction with the provided text, amount, and date");

app.MapGet("/transactions/{id:guid}", (Guid id) =>
{
    // In a real app, you'd retrieve from database
    var transaction = new
    {
        Id = id,
        Text = "Sample transaction",
        Amount = 99.99m,
        Date = DateOnly.FromDateTime(DateTime.Now),
        ProcessedAt = DateTime.UtcNow,
        Status = "Processed"
    };
    
    return Results.Ok(transaction);
})
.WithName("GetTransaction")
.WithSummary("Get a transaction by ID")
.WithDescription("Retrieves a specific transaction by its unique identifier");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record TransactionDto(string Text, decimal Amount, DateOnly Date);
