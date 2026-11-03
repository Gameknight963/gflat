using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.Properties;

namespace gflat.VisualStudio;

[ExportDynamicEnumValuesProvider(ProviderName)]
[Export(typeof(IDynamicDebugTargetsGenerator))]
[ExportMetadata("Name", ProviderName)]
[AppliesTo("Gflat")]
internal sealed class GflatDebugTargets : ChainedProjectValueDataSourceBase<IReadOnlyList<IEnumValue>>,
    IDynamicDebugTargetsGenerator, IDynamicEnumValuesProvider
{
    internal const string ProviderName = "GflatDebugTargetProvider";
    private readonly ConfiguredProject project;
    private readonly IProjectSubscriptionService subscriptions;

    [ImportingConstructor]
    public GflatDebugTargets(ConfiguredProject project, IProjectSubscriptionService subscriptions)
        : base(project, synchronousDisposal: false, registerDataSource: false)
    {
        this.project = project;
        this.subscriptions = subscriptions;
    }

    internal static IEnumValue Target(string projectPath) => new PageEnumValue(new EnumValue
    {
        Name = "Project",
        DisplayName = Path.GetFileNameWithoutExtension(projectPath).Replace("&", "&&")
    });

    public Task<IDynamicEnumValuesGenerator> GetProviderAsync(IList<NameValuePair>? options)
        => Task.FromResult<IDynamicEnumValuesGenerator>(new Values(() => Target(project.UnconfiguredProject.FullPath)));

    protected override IDisposable LinkExternalInput(ITargetBlock<IProjectVersionedValue<IReadOnlyList<IEnumValue>>> targetBlock)
    {
        var source = subscriptions.ProjectRuleSource;
        var transform = new TransformBlock<IProjectVersionedValue<IProjectSubscriptionUpdate>, IProjectVersionedValue<IReadOnlyList<IEnumValue>>>(
            update => update.Derive(_ => (IReadOnlyList<IEnumValue>)new[] { Target(project.UnconfiguredProject.FullPath) }));
        return new Links(source.SourceBlock.LinkTo(transform, new DataflowLinkOptions { PropagateCompletion = true }),
            transform.LinkTo(targetBlock, new DataflowLinkOptions { PropagateCompletion = true }), JoinUpstreamDataSources(source));
    }

    private sealed class Values : IDynamicEnumValuesGenerator
    {
        private readonly Func<IEnumValue> target;
        internal Values(Func<IEnumValue> target) { this.target = target; }
        public bool AllowCustomValues => false;
        public Task<ICollection<IEnumValue>> GetListedValuesAsync()
            => Task.FromResult<ICollection<IEnumValue>>(new[] { target() });
        public Task<IEnumValue?> TryCreateEnumValueAsync(string userSuppliedValue)
            => Task.FromResult(userSuppliedValue == "Project" ? target() : null);
    }

    private sealed class Links : IDisposable
    {
        private readonly IDisposable[] links;
        internal Links(params IDisposable[] links) { this.links = links; }
        public void Dispose() { foreach (var link in links) link.Dispose(); }
    }
}
