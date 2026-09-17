using Validation.Api;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5084");

builder.Services.AddValidationApi(builder.Configuration);

var app = builder.Build();
app.UseValidationApiPipeline();
app.MapValidationApi(scopeLoopbackFilter: false);
app.Run();

public partial class Program;
