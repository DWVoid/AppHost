using System.Reflection;

namespace Akarin.AppLib.Dependency;

public interface IScope
{
    object Resolve(Type type);

    T Result<T>() where T : class => (Resolve(typeof(T)) as T)!;
}

public sealed class InjectionConstructorAttribute : Attribute
{
}

public sealed class BadRegistrationException : Exception
{
    public enum Reason
    {
        Incompatible,
        IsAbstract,
        NotClass,
        AmbiguousCtor
    }

    public static object ReasonKey = new();
    public static object TargetKey = new();
    public static object OffendingKey = new();

    internal BadRegistrationException(Type target, Type offending, Reason reason) : base(GetMessage(target, offending,
        reason))
    {
        Data[ReasonKey] = reason;
        Data[TargetKey] = target;
        Data[OffendingKey] = offending;
    }

    private static string GetMessage(Type target, Type offending, Reason reason)
    {
        return reason switch
        {
            Reason.Incompatible => $"Type {offending.FullName} is not compatible with {target.FullName}",
            Reason.IsAbstract => $"Type {offending.FullName} is abstract class",
            Reason.NotClass => $"Type {offending.FullName} is not a class",
            Reason.AmbiguousCtor => $"Cannot find appropriate constructor for type {offending.FullName}",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
    }
}

public class Scope : IScope, IAsyncDisposable, IDisposable
{
    private readonly Scope? _parent;
    private readonly Dictionary<Type, IInstanceProvider> _lookup = new();

    public Scope(Scope parent)
    {
        _parent = parent;
    }

    ~Scope() => Task.Run(JoinAndDestroyScope).Wait();

    public object Resolve(Type type)
    {
        IInstanceProvider? provider = null;
        for (var scope = this; scope != null; scope = scope._parent)
        {
            lock (scope)
            {
                if (scope._lookup.TryGetValue(type, out provider)) break;
            }
        }

        if (provider == null) throw new ResolutionFailureException(type);
        return provider.GetInstance();
    }

    private static void CheckClass(Type concrete, Type of)
    {
        if (!concrete.IsAssignableTo(of))
            throw new BadRegistrationException(of, concrete, BadRegistrationException.Reason.Incompatible);
        if (!concrete.IsClass)
            throw new BadRegistrationException(of, concrete, BadRegistrationException.Reason.NotClass);
        if (!concrete.IsAbstract)
            throw new BadRegistrationException(of, concrete, BadRegistrationException.Reason.IsAbstract);
    }

    private static ConstructorInfo? ExamineConstructor(ConstructorInfo ctor)
    {
        foreach (var param in ctor.GetParameters())
        {
            if (param.IsIn) return null;
            if (param.IsOut) return null;
            var type = param.ParameterType;
            if (!type.IsClass || !type.IsInterface) return null;
        }

        return ctor;
    }

    private static ConstructorInfo? SelectConstructor(IReadOnlyList<ConstructorInfo> constructors)
    {
        if (constructors.Count == 1) return ExamineConstructor(constructors[0]);
        var matched = constructors
            .Where(o => o.GetCustomAttribute<InjectionConstructorAttribute>() != null)
            .ToArray();
        return matched.Length == 1 ? ExamineConstructor(matched[0]) : null;
    }

    private IInstanceProvider CreateFactoryProvider(ConstructorInfo info)
    {
        if (info.GetParameters().Length > 0)
            return new SimpleFactoryProvider(info);
        return new ResolvingFactoryProvider(this, info);
    }

    private IInstanceProvider CreateFactoryProviderOfTypeChecked(Type concrete, Type of)
    {
        CheckClass(concrete, of);
        var ctor = SelectConstructor(concrete.GetConstructors());
        if (ctor == null)
            throw new BadRegistrationException(of, concrete, BadRegistrationException.Reason.AmbiguousCtor);
        return CreateFactoryProvider(ctor);
    }

    private bool TryRegisterProvider(Type key, IInstanceProvider provider)
    {
        lock (this)
        {
            return _lookup.TryAdd(key, provider);
        }
    }

    public bool RegisterFactory(Type concrete, Type of)
    { 
        return TryRegisterProvider(of, CreateFactoryProviderOfTypeChecked(concrete, of));
    }

    public bool RegisterSingleton(Type concrete, Type of)
    {
        var factory = CreateFactoryProviderOfTypeChecked(concrete, of);
        return TryRegisterProvider(of, new LazyInstanceProvider(factory));
    }

    public bool RegisterSingleton(Type concrete, Type of, Action<object> dispose)
    {
        var factory = CreateFactoryProviderOfTypeChecked(concrete, of);
        return TryRegisterProvider(of, new LazyInstanceProvider(factory, dispose));
    }

    public bool RegisterSingleton(Type concrete, Type of, Func<object, ValueTask> dispose)
    {
        var factory = CreateFactoryProviderOfTypeChecked(concrete, of);
        return TryRegisterProvider(of, new LazyInstanceProvider(factory, dispose));
    }

    public bool Provide(object instance, Type of, bool owns = true)
    {
        CheckClass(instance.GetType(), of);
        
    }

    public bool RegisterFactory<T1, T2>() where T1 : T2 => RegisterFactory(typeof(T1), typeof(T2));

    private async ValueTask JoinAndDestroyScope()
    {
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Task.Run(JoinAndDestroyScope).Wait();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return JoinAndDestroyScope();
    }

    public void Close() => Dispose();

    public ValueTask CloseAsync() => DisposeAsync();
}