using NEventStore;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// Implement this to register a pipeline hook into NEventStore.  Alternatively <see cref="PostCommitNotifierPipeline"/>
    /// </summary>
	public abstract class CustomPipelineHook : PipelineHookBase { }
}
