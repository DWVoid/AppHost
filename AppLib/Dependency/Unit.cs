using System.Reflection;

namespace Akarin.AppLib.Dependency;

internal class FactoryUnit : IUnit
{
    private readonly ConstructorInfo _ctor;

    public FactoryUnit(ConstructorInfo ctor) => _ctor = ctor;

    public object? GetInstanceNow() => _ctor.Invoke(null);

    public Task<object> GetInstanceAsync() => Task.FromResult(_ctor.Invoke(null));
}

internal class SingletonUnit : IUnit
{
    public object? GetInstanceNow()
    {
        throw new NotImplementedException();
    }

    public Task<object> GetInstanceAsync()
    {
        throw new NotImplementedException();
    }
}

internal class InstanceUnit : IUnit
{
    private readonly object _obj;
    public InstanceUnit(object obj) => _obj = obj;
    public object GetInstanceNow() => _obj;
    public Task<object> GetInstanceAsync() => Task.FromResult(_obj);
}

