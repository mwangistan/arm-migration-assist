using AutomatedMigration.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAutomationApi(builder.Configuration);

var app = builder.Build();
app.UseAutomationApiPipeline(builder.Configuration);
app.MapAutomationApi();
app.Run();

public partial class Program { }
