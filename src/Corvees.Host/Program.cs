using Corvees.Host.Authentication;
using Corvees.Host.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCorvees();

var app = builder.Build();
HostConfiguration.Validate(app.Configuration);

app.UseExceptionHandler();
app.UseRouting();
app.UseMiddleware<MemberAuthenticationMiddleware>();

app.MapOperationalEndpoints();
app.MapControllers();
app.MapMcp("/m/{token}/mcp");
app.Run();

public partial class Program;
