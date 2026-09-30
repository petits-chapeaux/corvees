using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

public sealed class QueryRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();
    private bool _recording;

    public void Start()
    {
        _commands.Clear();
        _recording = true;
    }

    public string[] Stop()
    {
        _recording = false;
        return _commands.ToArray();
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (_recording)
            _commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
