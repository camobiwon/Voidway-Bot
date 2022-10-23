using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voidway_Bot
{
    // do whatever the fuck this shit is in order to get dsharpplus to use Logger.Put
    internal class DiscordLogger : ILogger
    {
        internal class Factory : ILoggerFactory
        {
            //List<ILoggerProvider> providers = new();
            public void AddProvider(ILoggerProvider provider)
            {
                //providers.Add(provider);
            }

            public ILogger CreateLogger(string categoryName)
            {
                DiscordLogger logger = new(categoryName);
                return logger;
            }

            public void Dispose() { }
        }

        static Logger.Reason ToReason(LogLevel level)
            => level switch
            {
                LogLevel.Debug => Logger.Reason.Trace,
                LogLevel.Information => Logger.Reason.Normal,
                LogLevel.Warning => Logger.Reason.Warn,
                LogLevel.Error => Logger.Reason.Fatal,
                LogLevel.Critical => Logger.Reason.Fatal,
                _ => throw new NotImplementedException(),
            };

        readonly string catName;

        private DiscordLogger(string catName)
        {
            this.catName = catName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel is LogLevel.None || logLevel is LogLevel.Trace) return false;

            if (logLevel is LogLevel.Debug && !Config.GetLogDiscordDebug()) return false;
            
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            Logger.Reason reason = ToReason(logLevel);
            if (eventId.Name is not null) reason.name = eventId.Name;
            Logger.Put(catName + " => " + message, reason);
        }
    }
}
