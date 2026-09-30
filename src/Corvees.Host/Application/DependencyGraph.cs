namespace Corvees.Host.Application;

public static class DependencyGraph
{
    public static bool Reaches(Guid from, Guid target, IEnumerable<(Guid From, Guid To)> edges)
    {
        var neighbors = edges.ToLookup(edge => edge.From, edge => edge.To);
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(from);
        while (pending.TryPop(out var current))
        {
            if (current == target)
                return true;
            if (!seen.Add(current))
                continue;
            foreach (var next in neighbors[current])
                pending.Push(next);
        }
        return false;
    }
}
