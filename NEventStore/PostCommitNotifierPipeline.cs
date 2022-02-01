using System;
using NEventStore;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// An NEvent pipeline that hooks into NEventStore and raises an event after every ICommit
    /// </summary>
    public class PostCommitNotifierPipeline : PipelineHookBase {
        /// <summary>
        /// An event which is raised after every local ICommit is created
        /// </summary>
        public event EventHandler<ICommit> AfterCommit;
        public override void PostCommit(ICommit committed) {
            // best practice to create variable referencing the handler because of thread safety issues 
            // https://stackoverflow.com/questions/3668953/raise-event-thread-safely-best-practice
            var evtHandler = AfterCommit;
            evtHandler?.Invoke(this, committed);
        }
    }
}