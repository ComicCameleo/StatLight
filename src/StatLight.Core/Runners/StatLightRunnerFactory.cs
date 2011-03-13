﻿
using StatLight.Core.Common.Abstractions.Timing;

namespace StatLight.Core.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web;
    using StatLight.Client.Harness.Events;
    using StatLight.Core.Configuration;
    using StatLight.Core.Common;
    using StatLight.Core.Events.Aggregation;
    using StatLight.Core.Monitoring;
    using StatLight.Core.Reporting.Providers.Console;
    using StatLight.Core.Reporting.Providers.TeamCity;
    using StatLight.Core.WebBrowser;
    using StatLight.Core.WebServer;
    using StatLight.Core.WebServer.XapHost;

    public class StatLightRunnerFactory
    {
        private readonly IEventSubscriptionManager _eventSubscriptionManager;
        private readonly IEventPublisher _eventPublisher;
        private BrowserCommunicationTimeoutMonitor _browserCommunicationTimeoutMonitor;
        private ConsoleResultHandler _consoleResultHandler;
        private Action<DebugClientEvent> _debugEventListener;

        public StatLightRunnerFactory() : this(new EventAggregator()) { }

        internal StatLightRunnerFactory(EventAggregator eventAggregator) : this(eventAggregator, eventAggregator) { }

        public StatLightRunnerFactory(IEventSubscriptionManager eventSubscriptionManager, IEventPublisher eventPublisher)
        {
            _eventSubscriptionManager = eventSubscriptionManager;
            _eventPublisher = eventPublisher;
        }

        public IRunner CreateContinuousTestRunner(ILogger logger, StatLightConfiguration statLightConfiguration)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            IWebServer webServer;
            List<IWebBrowser> webBrowsers;
            IDialogMonitorRunner dialogMonitorRunner;

            BuildAndReturnWebServiceAndBrowser(
                logger,
                statLightConfiguration.Server.ShowTestingBrowserHost,
                statLightConfiguration,
                out webServer,
                out webBrowsers,
                out dialogMonitorRunner);

            CreateAndAddConsoleResultHandlerToEventAggregator(logger);

            IRunner runner = new ContinuousConsoleRunner(logger, _eventSubscriptionManager, _eventPublisher, statLightConfiguration.Server.XapToTestPath, statLightConfiguration.Client, webServer, webBrowsers.First());
            return runner;
        }

        public IRunner CreateTeamCityRunner(StatLightConfiguration statLightConfiguration)
        {
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            ILogger logger = new NullLogger();
            IWebServer webServer;
            List<IWebBrowser> webBrowsers;
            IDialogMonitorRunner dialogMonitorRunner;

            BuildAndReturnWebServiceAndBrowser(
                logger,
                false,
                statLightConfiguration,
                out webServer,
                out webBrowsers,
                out dialogMonitorRunner);

            var teamCityTestResultHandler = new TeamCityTestResultHandler(new ConsoleCommandWriter(), statLightConfiguration.Server.XapToTestPath);
            IRunner runner = new TeamCityRunner(new NullLogger(), _eventSubscriptionManager, _eventPublisher, webServer, webBrowsers, teamCityTestResultHandler, statLightConfiguration.Server.XapToTestPath, dialogMonitorRunner);

            return runner;
        }

        public IRunner CreateOnetimeConsoleRunner(ILogger logger, StatLightConfiguration statLightConfiguration)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            IWebServer webServer;
            List<IWebBrowser> webBrowsers;
            IDialogMonitorRunner dialogMonitorRunner;

            BuildAndReturnWebServiceAndBrowser(
                logger,
                statLightConfiguration.Server.ShowTestingBrowserHost,
                statLightConfiguration,
                out webServer,
                out webBrowsers,
                out dialogMonitorRunner);

            CreateAndAddConsoleResultHandlerToEventAggregator(logger);
            IRunner runner = new OnetimeRunner(logger, _eventSubscriptionManager, _eventPublisher, webServer, webBrowsers, statLightConfiguration.Server.XapToTestPath, dialogMonitorRunner);
            return runner;
        }

        public IRunner CreateWebServerOnlyRunner(ILogger logger, StatLightConfiguration statLightConfiguration)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            var location = new WebServerLocation(logger);

            var webServer = CreateWebServer(logger, statLightConfiguration, location);
            CreateAndAddConsoleResultHandlerToEventAggregator(logger);
            SetupDebugClientEventListener(logger);
            IRunner runner = new WebServerOnlyRunner(logger, _eventSubscriptionManager, _eventPublisher, webServer, location.TestPageUrl, statLightConfiguration.Server.XapToTestPath);

            return runner;
        }


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private IWebServer CreateWebServer(ILogger logger, StatLightConfiguration statLightConfiguration, WebServerLocation webServerLocation)
        {

            var postHandler = new PostHandler(logger, _eventPublisher, statLightConfiguration.Client);

            //var statLightService = new StatLightService(logger, statLightConfiguration.Client, statLightConfiguration.Server, postHandler);
            //return new StatLightServiceHost(logger, statLightService, location.BaseUrl);

            var responseFactory = new ResponseFactory(statLightConfiguration.Server.HostXap, statLightConfiguration.Client);
            return new WebServer(logger, webServerLocation, responseFactory, postHandler);

        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private void BuildAndReturnWebServiceAndBrowser(
            ILogger logger,
            bool showTestingBrowserHost,
            StatLightConfiguration statLightConfiguration,
            out IWebServer webServer,
            out List<IWebBrowser> webBrowsers,
            out IDialogMonitorRunner dialogMonitorRunner)
        {
            ClientTestRunConfiguration clientTestRunConfiguration = statLightConfiguration.Client;
            ServerTestRunConfiguration serverTestRunConfiguration = statLightConfiguration.Server;

            var location = new WebServerLocation(logger);
            var debugAssertMonitorTimer = new TimerWrapper(serverTestRunConfiguration.DialogSmackDownElapseMilliseconds);
            SetupDebugClientEventListener(logger);
            webServer = CreateWebServer(logger, statLightConfiguration, location);

            webBrowsers = GetWebBrowsers(logger, location.TestPageUrl, clientTestRunConfiguration, showTestingBrowserHost, serverTestRunConfiguration.QueryString, statLightConfiguration.Server.ForceBrowserStart);

            dialogMonitorRunner = SetupDialogMonitorRunner(logger, webBrowsers, debugAssertMonitorTimer);

            StartupBrowserCommunicationTimeoutMonitor();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "testPageUrlWithQueryString")]
        private static List<IWebBrowser> GetWebBrowsers(ILogger logger, Uri testPageUrl, ClientTestRunConfiguration clientTestRunConfiguration, bool showTestingBrowserHost, string queryString, bool forceBrowserStart)
        {
            var webBrowserType = clientTestRunConfiguration.WebBrowserType;
            var webBrowserFactory = new WebBrowserFactory(logger);
            var testPageUrlWithQueryString = new Uri(testPageUrl + "?" + queryString);
            logger.Debug("testPageUrlWithQueryString = " + testPageUrlWithQueryString);
            List<IWebBrowser> webBrowsers = Enumerable
                .Range(1, clientTestRunConfiguration.NumberOfBrowserHosts)
                .Select(browserI => webBrowserFactory.Create(webBrowserType, testPageUrlWithQueryString, showTestingBrowserHost, forceBrowserStart, clientTestRunConfiguration.NumberOfBrowserHosts > 1))
                .ToList();
            return webBrowsers;
        }

        private void StartupBrowserCommunicationTimeoutMonitor()
        {
            if (_browserCommunicationTimeoutMonitor == null)
            {
                _browserCommunicationTimeoutMonitor = new BrowserCommunicationTimeoutMonitor(_eventPublisher, new TimerWrapper(3000), TimeSpan.FromMinutes(5));
                _eventSubscriptionManager.AddListener(_browserCommunicationTimeoutMonitor);
            }
        }

        private void CreateAndAddConsoleResultHandlerToEventAggregator(ILogger logger)
        {
            if (_consoleResultHandler == null)
            {
                _consoleResultHandler = new ConsoleResultHandler(logger);
                _eventSubscriptionManager.AddListener(_consoleResultHandler);
            }
        }

        private void SetupDebugClientEventListener(ILogger logger)
        {
            var ea = _eventSubscriptionManager as EventAggregator;
            if (ea != null)
                ea.Logger = logger;

            if (_debugEventListener == null)
            {
                _debugEventListener = e => logger.Debug(e.Message);
                _eventSubscriptionManager.AddListener(_debugEventListener);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public IRunner CreateRemotelyHostedRunner(ILogger logger, StatLightConfiguration statLightConfiguration)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");

            ClientTestRunConfiguration clientTestRunConfiguration = statLightConfiguration.Client;
            ServerTestRunConfiguration serverTestRunConfiguration = statLightConfiguration.Server;

            throw new NotImplementedException();
            //var urlToTestPage = statLightConfiguration.Client.XapToTestUrl.ToUri();

            //var location = new RemoteSiteOverriddenLocation(logger, urlToTestPage);
            //var debugAssertMonitorTimer = new TimerWrapper(serverTestRunConfiguration.DialogSmackDownElapseMilliseconds);
            //SetupDebugClientEventListener(logger);
            //var webServer = CreateWebServer(logger, statLightConfiguration, location);
            //
            //var showTestingBrowserHost = serverTestRunConfiguration.ShowTestingBrowserHost;
            //
            //var querystring = "?{0}={1}".FormatWith(StatLightServiceRestApi.StatLightResultPostbackUrl,
            //                                       HttpUtility.UrlEncode(location.BaseUrl.ToString()));
            //var testPageUrlAndPostbackQuerystring = new Uri(location.TestPageUrl + querystring);
            //logger.Debug("testPageUrlAndPostbackQuerystring={0}".FormatWith(testPageUrlAndPostbackQuerystring.ToString()));
            //var webBrowsers = GetWebBrowsers(logger, testPageUrlAndPostbackQuerystring, clientTestRunConfiguration, showTestingBrowserHost, serverTestRunConfiguration.QueryString, statLightConfiguration.Server.ForceBrowserStart);
            //
            //var dialogMonitorRunner = SetupDialogMonitorRunner(logger, webBrowsers, debugAssertMonitorTimer);
            //
            //StartupBrowserCommunicationTimeoutMonitor();
            //CreateAndAddConsoleResultHandlerToEventAggregator(logger);
            //
            //IRunner runner = new OnetimeRunner(logger, _eventSubscriptionManager, _eventPublisher, webServer, webBrowsers, statLightConfiguration.Server.XapToTestPath, dialogMonitorRunner);
            //return runner;
        }

        private IDialogMonitorRunner SetupDialogMonitorRunner(ILogger logger, List<IWebBrowser> webBrowsers, TimerWrapper debugAssertMonitorTimer)
        {
            var dialogMonitors = new List<IDialogMonitor>
                                     {
                                         new DebugAssertMonitor(logger),
                                     };

            foreach (var webBrowser in webBrowsers)
            {
                var monitor = new MessageBoxMonitor(logger, webBrowser);
                dialogMonitors.Add(monitor);
            }

            return new DialogMonitorRunner(logger, _eventPublisher, debugAssertMonitorTimer, dialogMonitors);
        }
    }
}
