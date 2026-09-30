using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Application;

public sealed record PageRequest
{
    public int Limit { get; }
    public int Offset { get; }

    public PageRequest(long limit = 50, string? cursor = null)
    {
        if (limit is < 1 or > 100)
            throw DomainException.Validation("limit must be between 1 and 100");

        Limit = (int)limit;
        if (cursor is null)
            return;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (!int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset > int.MaxValue - Limit)
                throw DomainException.Validation("Invalid cursor");
            Offset = offset;
        }
        catch (FormatException)
        {
            throw DomainException.Validation("Invalid cursor");
        }
    }
}

public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public static async Task<Page<T>> ReadAsync(IQueryable<T> query, PageRequest request, CancellationToken ct)
    {
        var items = await query.Skip(request.Offset).Take(request.Limit + 1).ToListAsync(ct);
        var hasMore = items.Count > request.Limit;
        if (hasMore)
            items.RemoveAt(request.Limit);

        var cursor = hasMore
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes((request.Offset + items.Count).ToString(CultureInfo.InvariantCulture)))
            : null;
        return new Page<T>(items, cursor);
    }
}
