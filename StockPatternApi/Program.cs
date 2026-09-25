using Microsoft.EntityFrameworkCore;
using StockPatternApi.Helpers;
using StockPatternApi.Services;

if (args.Contains("--stockpatternapibot-email-open"))
{
    await global::StockPatternApi.StockPatternAPIBotScan.EmailOpenUnfinalizedAsync();
    return;
}
if (args.Contains("--stockpatternapibot-email"))
{
    await global::StockPatternApi.StockPatternAPIBotScan.EmailLatestResultsAsync();
    return;
}

if (args.Contains("--stockpatternapibot-scan"))
{
    await global::StockPatternApi.StockPatternAPIBotScan.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Entity Framework Core with SQL Server
builder.Services.AddDbContext<StockPatternDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("StockPatternApi")));

builder.Services.AddTransient<EmailService>();
builder.Services.AddHttpClient<StockPatternScanService>((_, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("StockPatternAPIBotScan/1.0");
});

// Add CORS policy here
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalFrontend", policy =>
    {
        policy.WithOrigins("http://127.0.0.1:5500", "http://localhost:5500") // For Testing Purposes
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowLocalFrontend"); // Apply CORS policy
app.UseAuthorization();
app.MapControllers();

app.Run();
