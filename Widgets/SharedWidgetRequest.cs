namespace OpenTaskbar.Widgets;

/// <summary>Shares a provider read and its recent result across dock views.</summary>
internal sealed class SharedWidgetRequest<TKey, TResult>(TimeSpan reuseFor) where TKey : notnull
{
    private readonly Dictionary<TKey, (DateTimeOffset StartedAt, Task<TResult> Result)> _requests = [];

    public Task<TResult> ReadAsync(TKey key, Func<Task<TResult>> load, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Task<TResult> request;
        lock (_requests)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in _requests.Where(pair => pair.Value.Result.IsCompleted
                     && (pair.Value.Result.Status != TaskStatus.RanToCompletion
                         || now - pair.Value.StartedAt >= reuseFor)).Select(pair => pair.Key).ToArray())
            {
                _requests.Remove(expired);
            }

            if (_requests.TryGetValue(key, out var existing))
            {
                request = existing.Result;
            }
            else
            {
                request = load();
                _requests.Add(key, (now, request));
            }
        }

        // Closing a display only cancels that view's wait, since another display can still need the result.
        return request.WaitAsync(cancellation);
    }
}
