using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;

namespace Echo.Composition;

public sealed class MefComposition : IDisposable
{
    private readonly AggregateCatalog catalog;
    private readonly CompositionContainer container;

    public MefComposition(params Assembly[] roots)
    {
        var assemblies = new Dictionary<string, Assembly>();
        var pending = new Queue<Assembly>(roots);
        while (pending.TryDequeue(out var assembly))
        {
            if (!assemblies.TryAdd(assembly.FullName!, assembly))
            {
                continue;
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("Echo.", StringComparison.Ordinal) == true)
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
            }
        }

        catalog = new AggregateCatalog(assemblies.Values.Select(assembly => new AssemblyCatalog(assembly)));
        container = new CompositionContainer(catalog, CompositionOptions.DisableSilentRejection);
    }

    public void Register<T>(T value)
    {
        container.ComposeExportedValue(value);
    }

    public T Resolve<T>()
    {
        return container.GetExportedValue<T>()!;
    }

    public void Dispose()
    {
        container.Dispose();
        catalog.Dispose();
    }
}
