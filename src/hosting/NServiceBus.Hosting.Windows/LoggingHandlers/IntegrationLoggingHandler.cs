﻿
using System.Configuration;

namespace NServiceBus.Hosting.Windows.LoggingHandlers
{
    /// <summary>
    /// Handles logging configuration for the integration profile.
    /// </summary>
    public class IntegrationLoggingHandler : IConfigureLoggingForProfile<Integration>
    {
        void IConfigureLogging.Configure(IConfigureThisEndpoint specifier)
        {
            if (Logging.Loggers.Log4NetAdapter.Log4NetConfigurator.Log4NetExists)
                SetLoggingLibrary.Log4Net(null, Logging.Loggers.Log4NetAdapter.Log4NetAppenderFactory.CreateColoredConsoleAppender("Info"));
            //else if (Logging.Loggers.NLogAdapter.NLogConfigurator.NLogExists)
            //    SetLoggingLibrary.NLog(null, Logging.Loggers.NLogAdapter.TargetFactory.CreateColoredConsoleTarget());
            else
                Internal.ConfigureInternalLog4Net.Integration();
        }
    }
}