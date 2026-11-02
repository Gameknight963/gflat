using System.Reflection;
using Xunit.Sdk;

[assembly: gflat.Tests.ProfileTestExecution]

namespace gflat.Tests;

public sealed class ProfileTestExecution : BeforeAfterTestAttribute
{
    private static readonly AsyncLocal<IDisposable?> Measurement = new();

    public override void Before(MethodInfo methodUnderTest)
    {
        if (!PipelineTimings.Enabled) return;
        PipelineTimings.TestName = methodUnderTest.DeclaringType!.FullName + "." + methodUnderTest.Name;
        Measurement.Value = PipelineTimings.Measure("test");
    }

    public override void After(MethodInfo methodUnderTest)
    {
        Measurement.Value?.Dispose();
        Measurement.Value = null;
        PipelineTimings.TestName = null;
    }
}
