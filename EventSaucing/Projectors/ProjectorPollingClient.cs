using Dapper;
using NEventStore.Persistence;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CRIS.Hawkshead.NEventStore
{
    /*
    /// <summary>
    /// An implementation of an NEventStore polling client which works with async projection. 
    /// </summary>
    public class ProjectorPollingClient
    {
        private readonly ILogger logger;
        private readonly ISqlDbService sqlDbService;
        private readonly IPersistStreams persistStreams;
        private IConventionalProjector projector;

#pragma warning disable AMNF0001 // Asynchronous method name is not ending with 'Async'
        public Task PollingTask { get; set; }
#pragma warning restore AMNF0001 // Asynchronous method name is not ending with 'Async'
        public CancellationToken Cancellation { get; set; }

   
        public ProjectorPollingClient(ILogger logger, ISqlDbService sqlDbService, IPersistStreams persistStreams) {
            this.logger = logger.ForContext(GetType());
            this.sqlDbService = sqlDbService;
            this.persistStreams = persistStreams;
        }

        public string ProjectorName { get; private set; }

        /// <summary>
        /// Determines the projector's latest checkpoint
        /// </summary>
        /// <param name="projector"></param>
        /// <param name="projectorName"></param>
        /// <returns></returns>
        private void InitialiseProjectorCheckpoint(IConventionalProjector projector, string projectorName) {
            this.projector = projector;
            this.ProjectorName = projectorName;

            //get last checkpoint projected (if any)
            using (var con = sqlDbService.GetCRISHHConnection()) {
                con.Open();
                projector.LastCheckpoint = con.QueryFirstOrDefault<int>("SELECT [Checkpoint] FROM dbo.ProjectorStatus WHERE ProjectorName=@projectorName", new { projectorName });

                if (projector.LastCheckpoint == 0) {
                    //Persist a record if we need to
                    con.Execute("INSERT [dbo].[ProjectorStatus] ([ProjectorName], [Checkpoint]) SELECT @projectorName, 0 WHERE NOT EXISTS(SELECT * FROM dbo.ProjectorStatus WHERE ProjectorName=@projectorName)", new { projectorName });
                }
            }
        }

        /// <summary>
        /// Polls the commit store in a loop looking for commits to project.  Will poll forever unless cancelled.
        /// </summary>
        /// <param name="dispatcher"></param>
        /// <param name="projectorName"></param>
        /// <param name="cancellation"></param>
        /// <param name="delayMilliseconds">int number of ms to wait between polls</param>
        /// <returns></returns>
        private async Task PollAndProjectAsync(ConventionBasedEventDispatcher dispatcher, int delayMilliseconds) {
            long? currentcheckpoint = null;

            //loop until cancelled
            while (!Cancellation.IsCancellationRequested) {
                // Delay projection
                try {
                    // wait for requested delay
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), Cancellation);
                } catch (TaskCanceledException) {
                    // we can ignore this task cancellation exception, it just means an app shutdown request occurred during the Task.Delay task
                    return;
                }
                string stage = "PRE var commits = persistStreams.GetFrom(projector.LastCheckpoint);";

                try {

                    //get any commits since last polled
                    var commits = persistStreams.GetFrom(projector.LastCheckpoint);
                    stage = "POST var commits = persistStreams.GetFrom(projector.LastCheckpoint);";

                    foreach (var commit in commits) {
                        //stop projecting if cancelled
                        if (Cancellation.IsCancellationRequested) return;

                        currentcheckpoint = commit.CheckpointToken;

                        //project
                        stage = "PRE await dispatcher.ProjectAsync(commit, projector); ";

                        await dispatcher.ProjectAsync(commit, projector);

                        stage = "POST await dispatcher.ProjectAsync(commit, projector); ";

                        //record new checkpoint
                        projector.LastCheckpoint = commit.CheckpointToken;

                        stage = "PRE using (var con = sqlDbService.GetCRISHHConnection()) {";

                        using (var con = sqlDbService.GetCRISHHConnection()) {
                            await con.OpenAsync();
                            await con.ExecuteAsync("UPDATE [dbo].[ProjectorStatus] SET [Checkpoint] = @CheckpointToken WHERE ProjectorName=@projectorName", new { ProjectorName, commit.CheckpointToken });
                        }

                        stage = "POST using (var con = sqlDbService.GetCRISHHConnection()) {";

                    }
                } catch (Exception e) {
                    //ensure we catch all exceptions
                    logger.Error(e, $"Exception caught at stage {stage} when projector {ProjectorName} tried to project commit {currentcheckpoint?.ToString() ?? "no checkpoint??"}");
                }
            }
        }

        public void Start(IConventionalProjector projector, CancellationToken cancellation, int delayMilliseconds) {
            var projectorName = projector.GetType().FullName;
            Cancellation = cancellation;

            InitialiseProjectorCheckpoint(projector, projectorName);

            var dispatcher = new ConventionBasedEventDispatcher(projector, logger);

            PollingTask = Task.Run(()=> PollAndProjectAsync(dispatcher, delayMilliseconds));
        }
    }*/
}
