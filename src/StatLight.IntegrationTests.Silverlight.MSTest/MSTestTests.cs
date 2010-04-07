﻿
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using Microsoft.Silverlight.Testing;

namespace StatLight.IntegrationTests.Silverlight
{
    using System;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class MSTestTests : SilverlightTest
    {
        #region Passing Tests
        [TestClass]
        public class MSTestNestedClassTests
        {
            [TestMethod]
            public void this_should_be_a_passing_test()
            {
                Assert.IsTrue(true);
            }
        }

        [TestMethod]
        public void this_should_be_a_passing_test()
        {
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void this_should_also_be_a_passing_test()
        {
            Assert.IsTrue(true);
        }

        [TestMethod]
        [Asynchronous]
        public void should_be_able_to_EncueueCallback_with_asyncronous_test()
        {
            var eventClass = new SomeEventClass();
            bool eventRaised = false;
            eventClass.FiredEvent += (sender, e) => { eventRaised = true; };

            EnqueueCallback(eventClass.FireTheEvent);
            EnqueueCallback(() => Assert.IsTrue(eventRaised));

            EnqueueTestComplete();
        }

        #endregion

        #region Failing Tests
        [TestMethod]
        public void this_should_be_a_Failing_test()
        {
            Exception ex1;
            try
            {
                throw new Exception("exception1");
            }
            catch (Exception ex)
            {
                ex1 = ex;
            }
            Exception ex2;
            try
            {
                throw new Exception("exception2", ex1);
            }
            catch (Exception ex)
            {
                ex2 = ex;
            }
            throw ex2;
        }

        [TestMethod]
        [Asynchronous]
        [Timeout(1000)]
        public void Should_fail_due_to_async_test_timeout()
        {
            EnqueueCallback(() => System.Threading.Thread.Sleep(10000));

            EnqueueTestComplete();
        }



        [TestMethod]
        public void Should_fail_due_to_a_dialog_assertion()
        {
            Assert.IsTrue(true);
            Debug.Assert(false, "Should_fail_due_to_a_dialog_assertion - message");
        }


        #endregion

        #region Ignored test
        [TestMethod]
        [Ignore]
        public void this_should_be_an_Ignored_test()
        {
            throw new Exception("This test should have been ignored.");
        }
        #endregion


        [TestMethod]
        public void Should_fail_due_to_a_message_box_modal_dialog()
        {
            MessageBox.Show("Should_fail_due_to_a_message_box_modal_dialog - message");
        }
    }

    public class SomeEventClass
    {
        public event EventHandler FiredEvent = delegate { };

        public void FireTheEvent()
        {
            FiredEvent(this, EventArgs.Empty);
        }
    }
}
