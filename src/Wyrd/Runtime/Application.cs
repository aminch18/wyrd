using Wyrd.Processes;

namespace Wyrd.Runtime;

/// <summary>
/// What an application is made of — slices, publications, upcasters, projections and processes — and the message
/// types they use. Shared by every host: the in-memory runtime and the durable ones.
/// </summary>
internal sealed class Application(MessageTypes types)
{
    public Registry Registry { get; } = new();

    public MessageTypes Types { get; } = types;

    public void Add(ISliceHandler slice)
    {
        Registry.Add(slice);
        Register(slice.Command, slice.State, slice.Event, slice.Error);
    }

    public void Add(IPublicationHandler publication)
    {
        Registry.Add(publication);
        Register(publication.Source, publication.Target);
    }

    public void Add(IUpcasterHandler upcaster)
    {
        Registry.Add(upcaster);
        Register(upcaster.From.Type, upcaster.To.Type);
    }

    public void Add(IProjectionHandler projection)
    {
        Registry.Add(projection);
        Register([projection.View, .. projection.Events]);
    }

    public void Add(Process process)
    {
        Registry.Add(process);
        RegisterTypesOf(process);
    }

    /// <summary>Replaces a process with a new definition of the same name, keeping its instances: a redeploy.</summary>
    public void Redeploy(Process process)
    {
        Registry.Redeploy(process);
        RegisterTypesOf(process);
    }

    private void RegisterTypesOf(Process process)
    {
        Register(process.Trigger);
        foreach (var node in process.Nodes)
        {
            if (node is AwaitNode wait)
            {
                Register(wait.Event);
            }
            else if (node is StepNode step)
            {
                Register(step.Action.Output, step.Compensation?.Output);
            }
        }

        Register(process.Outcome?.Action.Output);
    }

    private void Register(params Type?[] types)
    {
        foreach (var type in types.OfType<Type>())
        {
            Types.Register(type);
        }
    }
}
