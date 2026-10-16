namespace gflat.Tests;

// These tests change process-wide tool discovery settings. They must not run
// alongside native builds whose configuration and cache keys read those settings.
[CollectionDefinition("Project SDK environment", DisableParallelization = true)]
public sealed class ProjectSdkEnvironmentCollection { }
