using System;
using System.Threading.Tasks;
using Cocoar.JsEval;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.Common;
#pragma warning restore CA1716

#pragma warning disable CA1720 // 'Guid', 'Random' property names match type names — JS-facing API, cannot rename
public class CommonModule : IJsModule
{
    public GuidHelper Guid { get; } = new();
    public Sleep Sleep { get; } = new();
    public Random Random { get; } = new();
}
#pragma warning restore CA1720

#pragma warning disable CA1720 // 'guid' parameter contains type name — JS-facing API, cannot rename
#pragma warning disable CA1822 // Instance methods required — Jint invokes these on a registered instance
public class GuidHelper
{
    public Guid Parse(string guid) => Guid.Parse(guid);
    public Guid New() => Guid.NewGuid();
    public Guid Empty() => Guid.Empty;
}
#pragma warning restore CA1822
#pragma warning restore CA1720

#pragma warning disable CA1822 // Instance methods required — Jint invokes these on a registered instance
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
#pragma warning restore CA1822
