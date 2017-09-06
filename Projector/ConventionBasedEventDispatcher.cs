using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using EventSaucing.NEventStore;
using NEventStore;
using Scalesque;

namespace EventSaucing.Projector {
    /// <summary>
    /// A convention based way of dispatching events to methods which project those events
    /// </summary>
    public class ConventionBasedEventDispatcher {
        private readonly Action<long> _setProjectorCheckpoint;

        /// <summary>
        /// This is a c sharp implementation of partial functions which are functions which can only operate on some subset of the input parameters  
        /// </summary>
        private class PartialFunction {
            /// <summary>
            /// Test if the Function can apply to the parameter object.  Returns true iif the Function can apply to the parameter
            /// </summary>
            public Func<object,bool> IsDefined { get; set; }
            /// <summary>
            /// The function which will be invoked if the partial function applies to the parameter
            /// </summary>
            public Action<IDbTransaction,ICommit, object> Function { get; set; }
        }

        readonly List<PartialFunction> _orderedPartialFunctions = new List<PartialFunction>();

        /// <summary>
        /// An optional predicate which determines if a commit can be projected
        /// </summary>
        private Option<Func<ICommit, Boolean>> _canProjectPredicate = Option.None();

        /// <summary>
        /// Instantiates the convent-based event projector
        /// </summary>
        /// <param name="setProjectorCheckpoint">An action to update the projector's checkpoint</param>
        public ConventionBasedEventDispatcher(Action<long> setProjectorCheckpoint) {
            _setProjectorCheckpoint = setProjectorCheckpoint;
        }

        public ConventionBasedEventDispatcher FirstProject<T>(Action<IDbTransaction, ICommit, T> a) {
            return AddPartialFunction(a);
        }

        /// <summary>
        /// Set an optional predicate to determine if a commit can be projected.  This predicate is tested before the the routing table is checked.
        /// </summary>
        /// <param name="f"></param>
        /// <returns></returns>
        public ConventionBasedEventDispatcher SetCanCommitPredicate(Func<ICommit, Boolean> f) {
            this._canProjectPredicate = f.ToSome();
            return this;
        }

        private ConventionBasedEventDispatcher AddPartialFunction<T>(Action<IDbTransaction, ICommit, T> a) {
            _orderedPartialFunctions.Add(new PartialFunction {
                IsDefined = o => o.GetType() == typeof (T),
                Function = (tx, commit, @event) => a(tx, commit, (T) @event)
            });

            return this;
        }

        public ConventionBasedEventDispatcher ThenProject<T>(Action<IDbTransaction, ICommit, T> a){
            return AddPartialFunction(a);
        }

        /// <summary>
        /// Are there any events in this commit which can be projected by this projector?
        /// </summary>
        /// <param name="commit"></param>
        /// <returns>bool</returns>
        public bool CanProject(ICommit commit) =>
            _canProjectPredicate.Map(f=>f(commit)).GetOrElse(true) && commit.Events.Any(eventMessage => eventMessage != null &&
                _orderedPartialFunctions.Any(pf => pf.IsDefined(eventMessage.Body)));

        /// <summary>
        /// Dispatchs the projectable events to the projection methods then updates the projector checkpoint
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="commit"></param>
        public void Project(IDbTransaction tx, ICommit commit) {
            foreach (var eventMessage in commit.Events.Where(eventMessage => eventMessage != null)) {
                foreach (var partialFunction in _orderedPartialFunctions) {
                    if (partialFunction.IsDefined(eventMessage.Body))
                        partialFunction.Function(tx, commit, eventMessage.Body);
                }
            }
            AdvanceProjectorCheckpoint(commit);
        }

        public void AdvanceProjectorCheckpoint(ICommit commit) {
            _setProjectorCheckpoint(commit.CheckpointTokenLong());
        }
    }
}
