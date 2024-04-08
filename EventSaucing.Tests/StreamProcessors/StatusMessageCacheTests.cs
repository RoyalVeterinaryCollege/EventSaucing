using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using EventSaucing.EventStream;
using FluentAssertions;
using NUnit.Framework;

namespace EventSaucing.StreamProcessors
{
    [TestFixture]
    public abstract class StatusMessageCacheTests {
       
        protected StatusMessageCache sut;

        public StatusMessageCacheTests()  {
            sut = new StatusMessageCache(5);

            Because();
        }

        protected virtual void Because() { }
    }

    public class WhenSingleStatusAdded : StatusMessageCacheTests  {
        private StreamProcessor.Messages.InternalState _msg;


        protected override void Because() {
            _msg = new StreamProcessor.Messages.InternalState( "test", DateTime.Now, 1,"Parp", new Dictionary<string, long>(), new Dictionary<string, long>(), true, "a message" );
            sut.AddStatus(_msg);
        }

        [Test]
        public void Should_stored_the_status() {
            sut.Statuses.First().Should().Be(_msg);
        }

        [Test]
        public void Should_have_count_of_one() {
            sut.Statuses.Where(x=>x != null).Should().HaveCount(1);
        }
    }

    public class WhenStatusOverFillTheCache : StatusMessageCacheTests  {
        protected override void Because() {
            // add 6 statuses with capacity of 5
            for (int i = 1; i < 7; i++) {
                sut.AddStatus(new StreamProcessor.Messages.InternalState ( "test", DateTime.Now, i, "Parp", new Dictionary<string, long>(), new Dictionary<string, long>(), true, "a message" ));
            }
        }

        [Test]
        public void Should_store_five_statuses() {
            sut.Statuses.Where(x=> x != null).Should().HaveCount(5);
        }

        // should have over written the first status with the last status

        [Test]
        public void Should_overwrite_first_checkpoint_with_last() {
            var x = sut.Statuses.First().Checkpoint.Should().Be(6);
        }

        [Test]
        public void Should_keep_second_status() {
            var x = sut.Statuses[1].Checkpoint.Should().Be(2);
        }
    }

}
