// 
// Copyright (c) 2004-2018 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

namespace NLog.UnitTests
{
    using System;
    using System.IO;
    using System.Threading;
    using NLog.Config;
    using Xunit;


    public class LogFactoryTests : NLogTestBase
    {
        [Fact]
        public void Flush_DoNotThrowExceptionsAndTimeout_DoesNotThrow()
        {
            LogManager.Configuration = CreateConfigurationFromString(@"
            <nlog throwExceptions='false'>
                <targets><target type='MethodCall' name='test' methodName='Throws' className='NLog.UnitTests.LogFactoryTests, NLog.UnitTests.netfx40' /></targets>
                <rules>
                    <logger name='*' minlevel='Debug' writeto='test'></logger>
                </rules>
            </nlog>");

            ILogger logger = LogManager.GetCurrentClassLogger();
            logger.Factory.Flush(_ => { }, TimeSpan.FromMilliseconds(1));
        }

        [Fact]
        public void InvalidXMLConfiguration_DoesNotThrowErrorWhen_ThrowExceptionFlagIsNotSet()
        {
            LogManager.ThrowExceptions = false;

            LogManager.Configuration = CreateConfigurationFromString(@"
            <nlog internalLogIncludeTimestamp='IamNotBooleanValue'>
                <targets><target type='MethodCall' name='test' methodName='Throws' className='NLog.UnitTests.LogFactoryTests, NLog.UnitTests.netfx40' /></targets>
                <rules>
                    <logger name='*' minlevel='Debug' writeto='test'></logger>
                </rules>
            </nlog>");
        }

        [Fact]
        public void InvalidXMLConfiguration_ThrowErrorWhen_ThrowExceptionFlagIsSet()
        {
            Boolean ExceptionThrown = false;
            try
            {
                LogManager.ThrowExceptions = true;

                LogManager.Configuration = CreateConfigurationFromString(@"
            <nlog internalLogIncludeTimestamp='IamNotBooleanValue'>
                <targets><target type='MethodCall' name='test' methodName='Throws' className='NLog.UnitTests.LogFactoryTests, NLog.UnitTests.netfx40' /></targets>
                <rules>
                    <logger name='*' minlevel='Debug' writeto='test'></logger>
                </rules>
            </nlog>");
            }
            catch (Exception)
            {
                ExceptionThrown = true;
            }

            Assert.True(ExceptionThrown);
        }

        [Fact]
        public void SecondaryLogFactoryDoesNotTakePrimaryLogFactoryLock()
        {
            File.WriteAllText("NLog.config", "<nlog />");
            try
            {
                bool threadTerminated;

                var primaryLogFactory = LogManager.factory;
                var primaryLogFactoryLock = primaryLogFactory._syncRoot;
                // Simulate a potential deadlock. 
                // If the creation of the new LogFactory takes the lock of the global LogFactory, the thread will deadlock.
                lock (primaryLogFactoryLock)
                {
                    var thread = new Thread(() =>
                    {
                        (new LogFactory()).GetCurrentClassLogger();
                    });
                    thread.Start();
                    threadTerminated = thread.Join(TimeSpan.FromSeconds(1));
                }

                Assert.True(threadTerminated);
            }
            finally
            {
                try
                {
                    File.Delete("NLog.config");
                }
                catch { }
            }
        }

        [Fact]
        public void ReloadConfigOnTimer_DoesNotThrowConfigException_IfConfigChangedInBetween()
        {
            EventHandler<LoggingConfigurationChangedEventArgs> testChanged = null;

            try
            {
                LogManager.Configuration = null;

                var loggingConfiguration = new LoggingConfiguration();
                LogManager.Configuration = loggingConfiguration;
                var logFactory = new LogFactory(loggingConfiguration);
                var differentConfiguration = new LoggingConfiguration();

                // Verify that the random configuration change is ignored (Only the final reset is reacted upon)
                bool called = false;
                LoggingConfiguration oldConfiguration = null, newConfiguration = null;
                testChanged = (s, e) => { called = true; oldConfiguration = e.DeactivatedConfiguration; newConfiguration = e.ActivatedConfiguration; };
                LogManager.LogFactory.ConfigurationChanged += testChanged;

                var exRecorded = Record.Exception(() => logFactory.ReloadConfigOnTimer(differentConfiguration));
                Assert.Null(exRecorded);

                // Final reset clears the configuration, so it is changed to null
                LogManager.Configuration = null;
                Assert.True(called);
                Assert.Equal(loggingConfiguration, oldConfiguration);
                Assert.Null(newConfiguration);
            }
            finally
            {
                if (testChanged != null)
                    LogManager.LogFactory.ConfigurationChanged -= testChanged;
            }
        }

        private class ReloadNullConfiguration : LoggingConfiguration
        {
            public override LoggingConfiguration Reload()
            {
                return null;
            }
        }

        [Fact]
        public void ReloadConfigOnTimer_DoesNotThrowConfigException_IfConfigReloadReturnsNull()
        {
            var loggingConfiguration = new ReloadNullConfiguration();
            LogManager.Configuration = loggingConfiguration;
            var logFactory = new LogFactory(loggingConfiguration);

            var exRecorded = Record.Exception(() => logFactory.ReloadConfigOnTimer(loggingConfiguration));
            Assert.Null(exRecorded);
        }

        [Fact]
        public void ReloadConfigOnTimer_Raises_ConfigurationReloadedEvent()
        {
            var called = false;
            var loggingConfiguration = new LoggingConfiguration();
            LogManager.Configuration = loggingConfiguration;
            var logFactory = new LogFactory(loggingConfiguration);
            logFactory.ConfigurationReloaded += (sender, args) => { called = true; };

            logFactory.ReloadConfigOnTimer(loggingConfiguration);

            Assert.True(called);
        }

        [Fact]
        public void ReloadConfigOnTimer_When_No_Exception_Raises_ConfigurationReloadedEvent_With_Correct_Sender()
        {
            object calledBy = null;
            var loggingConfiguration = new LoggingConfiguration();
            LogManager.Configuration = loggingConfiguration;
            var logFactory = new LogFactory(loggingConfiguration);
            logFactory.ConfigurationReloaded += (sender, args) => { calledBy = sender; };

            logFactory.ReloadConfigOnTimer(loggingConfiguration);

            Assert.Same(calledBy, logFactory);
        }

        [Fact]
        public void ReloadConfigOnTimer_When_No_Exception_Raises_ConfigurationReloadedEvent_With_Argument_Indicating_Success()
        {
            LoggingConfigurationReloadedEventArgs arguments = null;
            var loggingConfiguration = new LoggingConfiguration();
            LogManager.Configuration = loggingConfiguration;
            var logFactory = new LogFactory(loggingConfiguration);
            logFactory.ConfigurationReloaded += (sender, args) => { arguments = args; };

            logFactory.ReloadConfigOnTimer(loggingConfiguration);

            Assert.True(arguments.Succeeded);
        }

        /// <summary>
        /// We should be forward compatible so that we can add easily attributes in the future.
        /// </summary>
        [Fact]
        public void NewAttrOnNLogLevelShouldNotThrowError()
        {
            LogManager.Configuration = CreateConfigurationFromString(@"
            <nlog throwExceptions='true' imAnewAttribute='noError'>
                <targets><target type='file' name='f1' filename='test.log' /></targets>
                <rules>
                    <logger name='*' minlevel='Debug' writeto='f1'></logger>
                </rules>
            </nlog>");
        }

        [Fact]
        public void ValueWithVariableMustNotCauseInfiniteRecursion()
        {
            LogManager.Configuration = null;

            var filename = "NLog.config";
            File.WriteAllText(filename, @"
            <nlog>
                <variable name='dir' value='c:\mylogs' />
                <targets>
                    <target name='f' type='file' fileName='${var:dir}\test.log' />
                </targets>
                <rules>
                    <logger name='*' writeTo='f' />
                </rules>
            </nlog>");
            try
            {
                var x = LogManager.Configuration;
                //2nd call
                var config = new XmlLoggingConfiguration(filename);
            }
            finally
            {
                File.Delete(filename);
            }
        }

        [Fact]
        public void EnableAndDisableLogging()
        {
            LogFactory factory = new LogFactory();
#pragma warning disable 618
            // In order Suspend => Resume 
            Assert.True(factory.IsLoggingEnabled());
            factory.DisableLogging();
            Assert.False(factory.IsLoggingEnabled());
            factory.EnableLogging();
            Assert.True(factory.IsLoggingEnabled());
#pragma warning restore 618
        }

        [Fact]
        public void SuspendAndResumeLogging_InOrder()
        {
            LogFactory factory = new LogFactory();

            // In order Suspend => Resume [Case 1]
            Assert.True(factory.IsLoggingEnabled());
            factory.SuspendLogging();
            Assert.False(factory.IsLoggingEnabled());
            factory.ResumeLogging();
            Assert.True(factory.IsLoggingEnabled());

            // In order Suspend => Resume [Case 2]
            using (var factory2 = new LogFactory())
            {
                Assert.True(factory.IsLoggingEnabled());
                factory.SuspendLogging();
                Assert.False(factory.IsLoggingEnabled());
                factory.ResumeLogging();
                Assert.True(factory.IsLoggingEnabled());
            }
        }

        [Fact]
        public void SuspendAndResumeLogging_OutOfOrder()
        {
            LogFactory factory = new LogFactory();

            // Out of order Resume => Suspend => (Suspend => Resume)
            factory.ResumeLogging();
            Assert.True(factory.IsLoggingEnabled());
            factory.SuspendLogging();
            Assert.True(factory.IsLoggingEnabled());
            factory.SuspendLogging();
            Assert.False(factory.IsLoggingEnabled());
            factory.ResumeLogging();
            Assert.True(factory.IsLoggingEnabled());
        }


        [Theory]
        [InlineData("d:\\configfile", "d:\\configfile", "d:\\configfile")]
        [InlineData("nlog.config", "c:\\temp\\nlog.config", "c:\\temp\\nlog.config")] //exists
        [InlineData("nlog.config", "c:\\temp\\nlog2.config", "nlog.config")] //not existing, fallback
        public void GetConfigFile_absolutePath_loads(string filename, string accepts, string expected, string baseDir = "c:\\temp")
        {
            // Arrange
            var fileMock = new FileMock(f => f == accepts);
            var factory = new LogFactory(null, fileMock);
            var appDomain = LogFactory.CurrentAppDomain;

            try
            {
                LogFactory.CurrentAppDomain = new AppDomainMock(baseDir);

                // Act
                var result = factory.GetConfigFile(filename);

                // Assert
                Assert.Equal(expected, result);
            }
            finally
            {
                //restore
                LogFactory.CurrentAppDomain = appDomain;
            }

        }
    }
}
