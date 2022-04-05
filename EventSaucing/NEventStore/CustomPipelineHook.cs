using NEventStore;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// Implement this to register a pipeline hook into NEventStore.  You must register this with IoC before calling <see cref="StartupExtensions.AddEventSaucing"/>
    /// Note, any implementation is scoped to the local node.
    ///
    /// <seealso cref="PostCommitNotifierPipeline"/>
    /// </summary>
	public abstract class CustomPipelineHook : PipelineHookBase { }
}
