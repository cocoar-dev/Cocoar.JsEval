using System;
using System.Threading.Tasks;
using Cocoar.JsEval;

namespace Cocoar.JsEval.Module.Common;

public class CommonModule : IJsModule
{
    public GuidHelper Guid { get; } = new();
    public Sleep Sleep { get; } = new();
    public Random Random { get; } = new();
}

public class GuidHelper
{
    public Guid Parse(string guid) => Guid.Parse(guid);
    public Guid New() => Guid.NewGuid();
    public Guid Empty() => Guid.Empty;
}

public class Sleep
{
    public Task Milliseconds(int milliseconds)
    {
        return Task.Delay(milliseconds);
    }

    public Task Seconds(int seconds)
    {
        return Task.Delay(seconds * 1000);
    }

    public Task Minutes(int minutes)
    {
        return Task.Delay(minutes * 60 * 1000);
    }
}
