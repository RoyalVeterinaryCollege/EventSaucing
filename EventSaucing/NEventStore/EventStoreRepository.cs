﻿using System;
using System.Collections.Generic;
using System.Linq;
using NEventStore;
using NEventStore.Domain;
using NEventStore.Domain.Persistence;
using NEventStore.Persistence;
using Scalesque;

namespace EventSaucing.NEventStore
{
    /// <summary>
    /// Based on https://github.com/NEventStore/NEventStore/blob/V5.2.0/src/NEventStore/CommonDomain/Persistence/EventStore/EventStoreRepository.cs
    /// We had to create our own copy as we found commit events could be persisted as null.
    /// We filter out any null values coming out of the store and guard against any going in.
    ///
    /// This class is not thread safe as it caches snapshots and event streams in mem in a non-thread safe collection
    /// </summary>
    public class EventStoreRepository : IRepository {
        private const string AggregateTypeHeader = "AggregateType";

        private readonly IDetectConflicts _conflictDetector;

        private readonly IStoreEvents _eventStore;

        private readonly IConstructAggregates _factory;

        private readonly IDictionary<string, ISnapshot> _snapshots = new Dictionary<string, ISnapshot>();

        private readonly IDictionary<string, IEventStream> _streams = new Dictionary<string, IEventStream>();

        public EventStoreRepository(IStoreEvents eventStore, IConstructAggregates factory, IDetectConflicts conflictDetector) {
            _eventStore = eventStore;
            _factory = factory;
            _conflictDetector = conflictDetector;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual TAggregate GetById<TAggregate>(Guid id) where TAggregate : class, IAggregate {
            return GetById<TAggregate>(Bucket.Default, id);
        }

        public virtual TAggregate GetById<TAggregate>(Guid id, int versionToLoad) where TAggregate : class, IAggregate {
            return GetById<TAggregate>(Bucket.Default, id, versionToLoad);
        }

        public TAggregate GetById<TAggregate>(string bucketId, Guid id) where TAggregate : class, IAggregate {
            return GetById<TAggregate>(bucketId, id, int.MaxValue);
        }

        public TAggregate GetById<TAggregate>(string bucketId, Guid id, int versionToLoad) where TAggregate : class, IAggregate {
            ISnapshot snapshot = GetSnapshot(bucketId, id, versionToLoad);
            IEventStream stream = OpenStream(bucketId, id, versionToLoad, snapshot);
            IAggregate aggregate = GetAggregate<TAggregate>(snapshot, stream);

            ApplyEventsToAggregate(versionToLoad, stream, aggregate);

            return aggregate as TAggregate;
        }

        public virtual void Save(IAggregate aggregate, Guid commitId, Action<IDictionary<string, object>> updateHeaders) {
            Save(Bucket.Default, aggregate, commitId, updateHeaders);

        }

        public void Save(string bucketId, IAggregate aggregate, Guid commitId, Action<IDictionary<string, object>> updateHeaders) {
            Dictionary<string, object> headers = PrepareHeaders(aggregate, updateHeaders);
            while (true) {
                IEventStream stream = PrepareStream(bucketId, aggregate, headers);
                int commitEventCount = stream.CommittedEvents.Count;

                try {
                    stream.CommitChanges(commitId);
                    aggregate.ClearUncommittedEvents();
                    return;
                }
                catch (DuplicateCommitException) {
                    stream.ClearChanges();
                    return;
                }
                catch (ConcurrencyException e) {
                    var conflict = ThrowOnConflict(stream, commitEventCount);
                    stream.ClearChanges();

                    if (conflict) {
                        throw new ConflictingCommandException(e.Message, e);
                    }
                }
                catch (StorageException e) {
                    throw new PersistenceException(e.Message, e);
                }
            }
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposing) {
                return;
            }

            lock (_streams) {
                foreach (var stream in _streams) {
                    stream.Value.Dispose();
                }

                _snapshots.Clear();
                _streams.Clear();
            }
        }

        private static void ApplyEventsToAggregate(int versionToLoad, IEventStream stream, IAggregate aggregate) {
            if (versionToLoad == 0 || aggregate.Version < versionToLoad) {
                // This is where we need to filter out any null events.
                foreach (var @event in stream.CommittedEvents.Where(e => e != null).Select(x => x.Body)) {
                    aggregate.ApplyEvent(@event);
                }
            }
        }

        private IAggregate GetAggregate<TAggregate>(ISnapshot snapshot, IEventStream stream) {
            IMemento memento = snapshot == null ? null : snapshot.Payload as IMemento;
            return _factory.Build(typeof(TAggregate), stream.StreamId.ToGuid().Get(), memento);
        }

        private ISnapshot GetSnapshot(string bucketId, Guid id, int version) {
            ISnapshot snapshot;
            var snapshotId = bucketId + id;
            if (!_snapshots.TryGetValue(snapshotId, out snapshot)) {
                _snapshots[snapshotId] = snapshot = _eventStore.Advanced.GetSnapshot(bucketId, id, version);
            }

            return snapshot;
        }

        private IEventStream OpenStream(string bucketId, Guid id, int version, ISnapshot snapshot) {
            IEventStream stream;
            var streamId = bucketId + "+" + id;
            if (_streams.TryGetValue(streamId, out stream)) {
                return stream;
            }

            stream = snapshot == null
                ? _eventStore.OpenStream(bucketId, id, 0, version)
                : _eventStore.OpenStream(snapshot, version);

            return _streams[streamId] = stream;
        }

        private IEventStream PrepareStream(string bucketId, IAggregate aggregate, Dictionary<string, object> headers) {
            IEventStream stream;
            var streamId = bucketId + "+" + aggregate.Id;
            if (!_streams.TryGetValue(streamId, out stream)) {
                _streams[streamId] = stream = _eventStore.CreateStream(bucketId, aggregate.Id);
            }

            foreach (var item in headers) {
                stream.UncommittedHeaders[item.Key] = item.Value;
            }

            aggregate.GetUncommittedEvents()
                        .Cast<object>()
                        .Where(x => x != null) // Don't persist any null events. 
                        .Select(x => new EventMessage { Body = x })
                        .ToList()
                        .ForEach(stream.Add);

            return stream;
        }

        private static Dictionary<string, object> PrepareHeaders(
            IAggregate aggregate, Action<IDictionary<string, object>> updateHeaders) {
            var headers = new Dictionary<string, object>();

            headers[AggregateTypeHeader] = aggregate.GetType().FullName;
            if (updateHeaders != null) {
                updateHeaders(headers);
            }

            return headers;
        }

        private bool ThrowOnConflict(IEventStream stream, int skip) {
            IEnumerable<object> committed = stream.CommittedEvents.Skip(skip).Select(x => x.Body);
            IEnumerable<object> uncommitted = stream.UncommittedEvents.Select(x => x.Body);
            return _conflictDetector.ConflictsWith(uncommitted, committed);
        }
    }
}