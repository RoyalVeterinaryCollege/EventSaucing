using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using DotNetty.Common.Utilities;
using EventSaucing.EventStream;
using Google.Protobuf.WellKnownTypes;
using NUnit.Framework;
using Scalesque;
using Option = Scalesque.Option;
using FluentAssertions;

namespace EventSaucing.NEventStore {



    [TestFixture]
    public abstract class CheckpointOrderTests {
        protected CheckpointOrder _sut;


        public CheckpointOrderTests() {
            _sut = new CheckpointOrder();

            Because();
        }

        protected virtual void Because() { }

    }

    public class When_ordering_checkpoints : CheckpointOrderTests {
        [Test]
        public void None_is_earlier_than_all_possible_real_checkpoints() {
            _sut.Compare(Option.None(), long.MinValue.ToSome()).Should().Be(-1);
        }

        [Test]
        public void All_possible_real_checkpoints_are_after_none() {
            _sut.Compare(long.MinValue.ToSome(), Option.None()).Should().Be(1);
        }

        [Test]
        public void Should_order_real_checkpoints()  {
            _sut.Compare(2L.ToSome(), 1L.ToSome()).Should().Be(1);
            _sut.Compare(1L.ToSome(), 2L.ToSome()).Should().Be(-1);
        }

        [Test]
        public void Same_checkpoint_is_equal()
        {
            _sut.Compare(2L.ToSome(), 2L.ToSome()).Should().Be(0);

        }
    }
}
