using System.Security.Cryptography;
using System.Text;
using Corvees.Host.Application;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Authentication;

public sealed class MemberAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CorveesDbContext db, MemberSession session)
    {
        var mcp = context.Request.Path.StartsWithSegments("/m");
        var rest = context.Request.Path.StartsWithSegments("/api/v1") && context.Request.Path != "/api/v1";
        if (!mcp && !rest)
        {
            await next(context);
            return;
        }

        var token = mcp ? context.Request.RouteValues["token"] as string : BearerToken(context.Request);
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            context.Response.StatusCode = mcp ? StatusCodes.Status404NotFound : StatusCodes.Status401Unauthorized;
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var member = await db.Members.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TokenHash == hash && x.DeletedAt == null, context.RequestAborted);
        if (member is null)
        {
            context.Response.StatusCode = mcp ? StatusCodes.Status404NotFound : StatusCodes.Status401Unauthorized;
            return;
        }

        db.GroupId = member.GroupId;
        session.Authenticate(member);
        await next(context);
    }

    private static string? BearerToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : null;
    }
}
