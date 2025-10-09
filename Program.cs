using Scalar.AspNetCore;
using RulesEngine.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Rules Engine
builder.Services.AddScoped<RulesEngine.RulesEngine>();

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

app.MapPost("/transactions", async (TransactionDto transaction) =>
{
    // Define business rules
    var rules = new List<Rule>
    {
        new() {
            RuleName = "MondayValidation",
            ErrorMessage = "Transactions are not allowed on Mondays - API is closed",
            Expression = "Date.DayOfWeek != System.DayOfWeek.Monday",
            RuleExpressionType = RuleExpressionType.LambdaExpression
        },
        new() {
            RuleName = "WeekendDiscount",
            SuccessEvent = "ApplyWeekendDiscount",
            Expression = "Date.DayOfWeek == System.DayOfWeek.Saturday || Date.DayOfWeek == System.DayOfWeek.Sunday",
            RuleExpressionType = RuleExpressionType.LambdaExpression
        }
    };
    
    var workflow = new Workflow
    {
        WorkflowName = "TransactionProcessing",
        Rules = rules
    };
    
    // Create Rules Engine instance with workflows
    var rulesEngine = new RulesEngine.RulesEngine([workflow]);
    
    // Execute rules
    var ruleResults = await rulesEngine.ExecuteAllRulesAsync("TransactionProcessing", transaction);
    
    // Check if Monday validation failed
    var mondayRule = ruleResults.FirstOrDefault(r => r.Rule.RuleName == "MondayValidation");
    if (mondayRule != null && !mondayRule.IsSuccess)
    {
        return Results.BadRequest(new
        {
            Error = mondayRule.Rule.ErrorMessage,
            RuleResults = ruleResults.Select(r => new
            {
                r.Rule.RuleName,
                r.IsSuccess,
                r.Rule.ErrorMessage
            })
        });
    }
    
    // Apply weekend discount if applicable
    var finalAmount = transaction.Amount;
    var weekendRule = ruleResults.FirstOrDefault(r => r.Rule.RuleName == "WeekendDiscount");
    if (weekendRule != null && weekendRule.IsSuccess)
    {
        finalAmount = transaction.Amount * 0.9m; // 10% discount
    }
    
    // Process the transaction
    var result = new
    {
        Id = Guid.NewGuid(),
        transaction.Text,
        OriginalAmount = transaction.Amount,
        FinalAmount = finalAmount,
        DiscountApplied = finalAmount != transaction.Amount,
        transaction.Date,
        ProcessedAt = DateTime.UtcNow,
        Status = "Processed",
        RuleResults = ruleResults.Select(r => new
        {
            r.Rule.RuleName,
            r.IsSuccess,
            Description = r.Rule.RuleName == "MondayValidation" ? "Validates that transactions are not on Mondays" :
                         r.Rule.RuleName == "WeekendDiscount" ? "Applies 10% discount for weekend transactions" : ""
        })
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
