using Scalar.AspNetCore;
using RulesEngine.Models;
using RulesEngine.Actions;

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
    var forecast = Enumerable.Range(1, 5).Select(index =>
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

app.MapGet("/years", async () =>
{
 var years = Enumerable.Range(2020, 5);

    var data = new[]
    {
        new YearData { Year = 2020, Text = "test1.01" },
        new YearData { Year = 2020, Text = "test1.02" },
        new YearData { Year = 2021, Text = "test2.01" },
        new YearData { Year = 2022, Text = "test3.01" },
        new YearData { Year = 2022, Text = "test3.02" },
        new YearData { Year = 2024, Text = "test4.01" },
        new YearData { Year = 2024, Text = "test4.02" },
    };

    var maxAllowedPerYear = 2;

    var rules = new List<Rule>
    {
        new() {
            RuleName = "MaxAllowedPerYear",
            Expression = "Data.Where(x => x.Year == Year).Count() < Convert.ToInt32(maxAllowedPerYear)",
            RuleExpressionType = RuleExpressionType.LambdaExpression,
            ErrorMessage = "Maximum allowed data per year reached"
        }
    };

    var workflow = new Workflow
    {
        WorkflowName = "YearValidation",
        Rules = rules
    };

    var rulesEngine = new RulesEngine.RulesEngine([workflow]);

    var validYears = new List<int>();
    foreach (var year in years)
    {
        var inputs = new[]
        {
            new RuleParameter("Year", year),
            new RuleParameter("Data", data),
            new RuleParameter("maxAllowedPerYear", maxAllowedPerYear)
        };

        var result = await rulesEngine.ExecuteAllRulesAsync("YearValidation", inputs);
        if (result.All(r => r.IsSuccess))
        {
            validYears.Add(year);
        }
    }

    return validYears;
})
.WithName("GetYears")
.WithDescription("Gets a list of years from 2000 to 2024")
.WithSummary("Get Years");

app.MapPost("/transactions", async (TransactionDto transaction) =>
{
    // Define business rules
    var rules = new List<Rule>
    {
        new() {
            ErrorMessage = "Transactions are not allowed on Mondays - API is closed",
            Expression = "Date.DayOfWeek != System.DayOfWeek.Monday",
            RuleExpressionType = RuleExpressionType.LambdaExpression,
            RuleName = "MondayValidation",
            SuccessEvent = "10",
        },
        new() {
            Expression = "Date.DayOfWeek == System.DayOfWeek.Saturday || Date.DayOfWeek == System.DayOfWeek.Sunday",
            RuleExpressionType = RuleExpressionType.LambdaExpression,
            RuleName = "WeekendDiscount",
            SuccessEvent = "20",
        },
        new() {
            ErrorMessage = "Transaction date must be at least 3 days in the past",
            Expression = "Date <= DateOnly.FromDateTime(DateTime.Now.AddDays(-3))",
            RuleExpressionType = RuleExpressionType.LambdaExpression,
            RuleName = "MinimumAgeValidation",
            SuccessEvent = "30",
            Actions = new RuleActions {
                OnSuccess = new ActionInfo {
                    Name = "ApplyHistoricalDataBonus",
                    Context = new Dictionary<string, object> {
                        { "DiscountPercent", 10 },
                        { "Reason", "Historical data bonus" }
                    }
                }
            }
        },
    };

    // Define custom actions
    var reSettings = new ReSettings
    {
        CustomActions = new Dictionary<string, Func<ActionBase>>
        {
            { "ApplyHistoricalDataBonus", () => new HistoricalDataBonusAction() }
        }
    };

    var workflow = new Workflow
    {
        WorkflowName = "TransactionProcessing",
        Rules = rules
    };

    // Create Rules Engine instance with workflows
    var rulesEngine = new RulesEngine.RulesEngine([workflow], reSettings);

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

    // Check if minimum age validation failed
    var ageRule = ruleResults.FirstOrDefault(r => r.Rule.RuleName == "MinimumAgeValidation");
    if (ageRule != null && !ageRule.IsSuccess)
    {
        return Results.BadRequest(new
        {
            Error = ageRule.Rule.ErrorMessage,
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

    // Apply historical data bonus if applicable (only fires when date is 3+ days old AND amount > 50)
    var bonusRule = ruleResults.FirstOrDefault(r => r.Rule.RuleName == "HistoricalDataBonus");
    if (bonusRule != null && bonusRule.IsSuccess)
    {
        finalAmount = finalAmount * 0.95m; // Additional 5% discount for historical high-value transactions
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
                         r.Rule.RuleName == "WeekendDiscount" ? "Applies 10% discount for weekend transactions" :
                         r.Rule.RuleName == "MinimumAgeValidation" ? "Validates that transaction date is at least 3 days in the past" :
                         r.Rule.RuleName == "HistoricalDataBonus" ? "Applies 5% bonus discount for historical transactions over $50" : ""
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

// Custom action class
public class HistoricalDataBonusAction : ActionBase
{
    public override ValueTask<object> Run(ActionContext context, RuleParameter[] ruleParameters)
    {
        // Access the context passed from the rule
        var discountPercent = context.GetContext<int>("DiscountPercent");
        var reason = context.GetContext<string>("Reason");

        // Log or perform custom logic
        Console.WriteLine($"Applying {discountPercent}% discount. Reason: {reason}");

        // You can also access the transaction data from ruleParameters
        // and perform calculations or side effects here

        return new ValueTask<object>(new { Success = true, DiscountPercent = discountPercent });
    }
}

public record YearData
{
    public int Year { get; set; }
    public string Text { get; set; }
}