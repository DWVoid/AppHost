namespace Akarin.AppLib.Dependency;

internal interface IUnit
{
    object? GetInstanceNow();
    Task<object> GetInstanceAsync();
}

internal delegate void AddDispose(object self, IFinalize method, object[] deps);

internal struct PendingUnit
{
    public readonly Type[] Dependencies;
    public readonly Func<IUnit[], AddDispose, IUnit> Factory;

    public PendingUnit(Type[] dependencies, Func<IUnit[], AddDispose, IUnit> factory)
    {
        Dependencies = dependencies;
        Factory = factory;
    }
}

public sealed class ResolutionFailureException : Exception
{
    public static object Key = new();

    internal ResolutionFailureException(Type request) : base($"Failed to resolve {request.FullName}")
    {
        Data[Key] = request;
    }
}

internal struct Container : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<Type, IUnit> _resolved = new();
    private readonly Dictionary<Type, PendingUnit> _pending = new();

    public Container()
    {
    }

    public bool TryAdd(Type type, PendingUnit unit)
    {
        lock (_lock) return !_resolved.ContainsKey(type) && _pending.TryAdd(type, unit);
    }

    public object? GetInstanceNow(Type type)
    {
        var unit = ResolveOne(type);
        return unit.GetInstanceNow();
    }

    public Task<object> GetInstanceAsync(Type type)
    {
        var unit = ResolveOne(type);
        return unit.GetInstanceAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Task[] final;
        lock (_disposeRoots)
        {
            final = new Task[_disposeRoots.Count];
            var index = 0;
            foreach (ref var (_, item) in _disposeRoots)
            {
                item.DispatchDispose();
                final[index++] = item.Finalize.Task;
            }
        }
        foreach (var task in final) await task;
    }

    private IUnit ResolveOne(Type type)
    {
        lock (_lock) return ResolveOneUnsafe(type);
    }

    private IUnit ResolveOneUnsafe(Type type)
    {
        if (_resolved.TryGetValue(type, out var resolved)) return resolved;
        if (!_pending.TryGetValue(type, out var pending)) throw new ResolutionFailureException(type);
        var unit = pending.Factory(pending.Dependencies.Select(ResolveOneUnsafe).ToArray(), AddDispose);
        _resolved.Add(type, unit);
        _pending.Remove(type);
        return unit;
    }

    private readonly struct FinalItem
    {
        public readonly IFinalize Finalize;
        public readonly List<Task> Predecessors = new();
        public FinalItem(IFinalize finalize) => Finalize = finalize;

        public async void DispatchDispose()
        {
            foreach (var task in Predecessors) await task;
            Finalize.Invoke();
        }
    }
    
    private readonly Dictionary<object, FinalItem> _disposeRoots = new();
    
    private void AddDispose(object self, IFinalize method, object[] deps)
    {
        var task = method.Task;
        lock (_disposeRoots)
        {
            foreach (var dep in deps)
            {
                if (!_disposeRoots.Remove(dep, out var item)) continue;
                item.Predecessors.Add(task);
            }

            _disposeRoots.Add(self, new FinalItem(method));
        }
    }
}