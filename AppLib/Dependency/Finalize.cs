namespace Akarin.AppLib.Dependency;

internal interface IFinalize
{
    Task Task { get; }
    void Invoke();
}

internal class FinalizeCreator
{
    private abstract class SyncFinalizeBase : IFinalize
    {
        private readonly TaskCompletionSource _source = new();
        public Task Task => _source.Task;
        public void Invoke()
        {
            try
            {
                InvokeImpl();
            }
            catch
            {
                // ignored
            }

            _source.SetResult();
        }

        protected abstract void InvokeImpl();
    }
    
    
    private abstract class AsyncFinalizeBase : IFinalize
    {
        private readonly TaskCompletionSource _source = new();
        public Task Task => _source.Task;
        public async void Invoke()
        {
            try
            {
                await InvokeImpl();
            }
            catch
            {
                // ignored
            }

            _source.SetResult();
        }

        protected abstract ValueTask InvokeImpl();
    }
    
    private class AutoFinalizeSync : SyncFinalizeBase
    {
        private readonly object _obj;
        public AutoFinalizeSync(object obj) => _obj = obj;
        protected override void InvokeImpl() => (_obj as IDisposable)!.Dispose();
    }

    private class AutoFinalizeAsync : AsyncFinalizeBase
    {
        private readonly object _obj;
        public AutoFinalizeAsync(object obj) => _obj = obj;
        protected override ValueTask InvokeImpl() => (_obj as IAsyncDisposable)!.DisposeAsync();
    }

    private class SyncFinalize : SyncFinalizeBase
    {
        private readonly object _obj;
        private readonly Action<object> _func;

        public SyncFinalize(object obj, Action<object> func)
        {
            _func = func;
            _obj = obj;
        }

        protected override void InvokeImpl() => _func(_obj);
    }

    private class TaskAsyncFinalize : IFinalize
    {
        private readonly object _obj;
        private readonly Func<object, Task> _func;
        
        private readonly TaskCompletionSource _source = new();

        public TaskAsyncFinalize(object obj, Func<object, Task> func)
        {
            _obj = obj;
            _func = func;
        }

        public Task Task => _source.Task;
        public async void Invoke()
        {
            try
            {
                await _func(_obj);
            }
            catch
            {
                // ignored
            }

            _source.SetResult();
        }
    }

    private class ValueTaskAsyncFinalize : AsyncFinalizeBase
    {
        private readonly object _obj;
        private readonly Func<object, ValueTask> _func;

        public ValueTaskAsyncFinalize(object obj, Func<object, ValueTask> func)
        {
            _obj = obj;
            _func = func;
        }
        
        protected override ValueTask InvokeImpl() => _func(_obj);
    }

    public static IFinalize? Create(object obj, Type info)
    {
        if (info.IsAssignableTo(typeof(IAsyncDisposable))) return new AutoFinalizeAsync(obj);
        if (info.IsAssignableTo(typeof(IDisposable))) return new AutoFinalizeSync(obj);
        return null;
    }

    public static IFinalize Create(object obj, Action<object> func) => new SyncFinalize(obj, func);

    public static IFinalize Create(object obj, Func<object, Task> func) => new TaskAsyncFinalize(obj, func);

    public static IFinalize Create(object obj, Func<object, ValueTask> func) => new ValueTaskAsyncFinalize(obj, func);
}