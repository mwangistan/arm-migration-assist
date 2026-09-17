using MigrationPlanner.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMigrationPlannerApi(builder.Configuration, builder.Environment.ContentRootPath);

var app = builder.Build();
app.UseMigrationPlannerPipeline();
app.MapMigrationPlannerApi();
app.Run();

namespace MigrationPlanner.Api
{
    public partial class Program
    {
        internal static string ResolveDefaultCorpusRoot(string contentRoot) =>
            MigrationPlannerHost.ResolveDefaultCorpusRoot(contentRoot);
    }
}
