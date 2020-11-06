﻿using Scalesque;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    public abstract class Reactor : IReactor {
        /// <summary>
        /// The version of the reactor
        /// </summary>
        public int VersionNumber { get; set; } = 1;

        /// <summary>
        /// The reactor's id
        /// </summary>
        public Option<long> Id { get; set; } = Option.None();

        /// <summary>
        /// The reactor's hidden state
        /// </summary>
        public abstract object State { get; set; }

        public virtual Task ReactAsync(ReactorActor.LocalMessages.SubscribedAggregateChanged msg, IUnitOfWork uow) => Task.CompletedTask;

        public virtual Task ReactAsync(ReactorActor.LocalMessages.ArticlePublished msg, IUnitOfWork uow) => Task.CompletedTask;
        /// <summary>
        /// Gets the last aggregate stream revision that was applied to the reactor.  If event stream was never applied, returns 0
        /// </summary>
        /// <param name="uow"></param>
        /// <param name="aggregateId"></param>
        /// <returns></returns>
        protected int GetLastAppliedStreamRevision(IUnitOfWork uow, Guid aggregateId) => uow.Previous.Map(previous => previous.AggregateSubscriptions.First(x => x.AggregateId == aggregateId).StreamRevision).GetOrElse(0);
    }
}
