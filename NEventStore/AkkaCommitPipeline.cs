using System;
using System.Threading;
using Akka.Actor;
using EventSaucing.Akka.Messages;
using EventSaucing.DependencyInjection.Autofac;
using NEventStore;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// This class hooks into the NEventStore pipeline and sends the commits to the LocalProjectionSupervisorActor in Akka
    /// </summary>
    class AkkaCommitPipeline : PipelineHookBase {
        private readonly Func<ActorPaths> _getPaths;
        private readonly Func<ActorSystem> _getAkka;

        private ActorPath _pathToProjectionSupervisor;
        private ActorSystem _actorSystem;
        private bool _isInitialised = false;
        private int _isInitialising = 0;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="getPaths"></param>
        /// <param name="getAkka"></param>
        /// <remarks>This class requires a func because NEventstore doesn't appear to allow hooking into the pipeline after instantiation.
        /// However, we don't want to create the actor system in a partially initialised state either and it has a 
        /// dependency on the event store.  Therefore, this func allows both NEventstore and actor system to created
        /// in a fully initialised state with the proviso that:  if a commit is made before the func is able to return
        /// the app might blow up!</remarks>
        public AkkaCommitPipeline(Func<ActorPaths> getPaths, Func<ActorSystem> getAkka) {
            _getPaths = getPaths;
            _getAkka = getAkka;
        }

        public override void PostCommit(ICommit committed) {
            if (_isInitialised) Notify(committed);
            else {
                if (Interlocked.CompareExchange(ref _isInitialising, 1, 0) == 0) { //ensure only one thread is initialising at once
                    _pathToProjectionSupervisor = _getPaths().LocalCommitSerialisor;
                    _actorSystem = _getAkka();
                    _isInitialised = true;
                    Interlocked.Exchange(ref _isInitialising, 0);
                    Notify(committed);
                }
                else {
                    //lost this commit as another thread is initialising
                }
            }
        }

        private void Notify(ICommit committed) {
            _actorSystem.ActorSelection(_pathToProjectionSupervisor).Tell(new CommitNotification(committed));
        }
    }
}