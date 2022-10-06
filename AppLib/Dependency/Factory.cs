using System.Reflection;

namespace Akarin.AppLib.Dependency;

internal interface IFactory
{
    bool IsAsync { get; }
    object GetInstance(object?[]? param);
    Task<object> GetInstanceAsync(object?[]? param);
}

internal class FactoryCreator
{
    private class SimpleFactory : IFactory
    {
        private readonly ConstructorInfo _info;

        public SimpleFactory(ConstructorInfo info) => _info = info;

        public bool IsAsync => false;

        public object GetInstance(object?[]? param) => _info.Invoke(param);

        public Task<object> GetInstanceAsync(object?[]? param) => Task.FromResult(GetInstance(param));
    }

    private class SyncInitFactory : IFactory
    {
        private readonly ConstructorInfo _info;
        private readonly Action<object> _init;

        public SyncInitFactory(ConstructorInfo info, Action<object> init)
        {
            _info = info;
            _init = init;
        }

        public bool IsAsync => false;

        public object GetInstance(object?[]? param)
        {
            var obj = _info.Invoke(param);
            _init(obj);
            return obj;
        }

        public Task<object> GetInstanceAsync(object?[]? param) => Task.FromResult(GetInstance(param));
    }

    private class TaskAsyncInitFactory : IFactory
    {
        private readonly ConstructorInfo _info;
        private readonly Func<object, Task> _init;

        public TaskAsyncInitFactory(ConstructorInfo info, Func<object, Task> init)
        {
            _info = info;
            _init = init;
        }

        public bool IsAsync => true;

        public object GetInstance(object?[]? param) => throw new NotImplementedException();

        public async Task<object> GetInstanceAsync(object?[]? param)
        {
            var obj = _info.Invoke(param);
            await _init(obj);
            return obj;
        }
    }

    private class ValueTaskAsyncInitFactory : IFactory
    {
        private readonly ConstructorInfo _info;
        private readonly Func<object, ValueTask> _init;

        public ValueTaskAsyncInitFactory(ConstructorInfo info, Func<object, ValueTask> init)
        {
            _info = info;
            _init = init;
        }

        public bool IsAsync => true;

        public object GetInstance(object?[]? param) => throw new NotImplementedException();

        public async Task<object> GetInstanceAsync(object?[]? param)
        {
            var obj = _info.Invoke(param);
            await _init(obj);
            return obj;
        }
    }

    public static IFactory Create(ConstructorInfo info) => 
        new SimpleFactory(info);
    
    public static IFactory Create(ConstructorInfo info, Action<object> init) => 
        new SyncInitFactory(info, init);
    
    public static IFactory Create(ConstructorInfo info, Func<object, Task> init) =>
        new TaskAsyncInitFactory(info, init);

    public static IFactory Create(ConstructorInfo info, Func<object, ValueTask> init) =>
        new ValueTaskAsyncInitFactory(info, init);
}