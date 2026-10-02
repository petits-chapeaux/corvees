using Corvees.Host.Authentication;
using Corvees.Host.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCorvees();
builder.Services.AddPublicHosting(builder.Configuration);

var app = builder.Build();
HostConfiguration.Validate(app.Configuration);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRouting();
if (!app.Environment.IsDevelopment())
    app.UseRateLimiter();
app.UseMiddleware<MemberAuthenticationMiddleware>();

app.MapOperationalEndpoints();
app.MapControllers();
app.MapMcp("/m/{token}/mcp");
app.Run();

public partial class Program;
