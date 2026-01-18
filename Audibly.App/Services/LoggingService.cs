// Author: rstewa · https://github.com/rstewa
// Created: 4/13/2024
// Updated: 01/18/2026 - Removed Sentry dependency

using System;
using System.IO;
using System.Text;
using Audibly.App.Services.Interfaces;

namespace Audibly.App.Services;

public class LoggingService(string logFilePath) : IloggingService
{
    #region IloggingService Members

    public void Log(string message)
    {
        try
        {
            var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}";
            File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
        }
        catch
        {
            // Swallow if file logging fails to prevent cascading errors
        }
    }

    public void LogError(Exception e, bool includeStackTrace = true)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ERROR: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Message: {e.Message}");
            sb.AppendLine($"Type: {e.GetType().FullName}");
            
            if (includeStackTrace && !string.IsNullOrEmpty(e.StackTrace))
            {
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(e.StackTrace);
            }
            
            // Log inner exceptions
            var innerEx = e.InnerException;
            int depth = 1;
            while (innerEx != null && depth <= 5)
            {
                sb.AppendLine($"--- Inner Exception {depth} ---");
                sb.AppendLine($"Message: {innerEx.Message}");
                sb.AppendLine($"Type: {innerEx.GetType().FullName}");
                if (includeStackTrace && !string.IsNullOrEmpty(innerEx.StackTrace))
                {
                    sb.AppendLine(innerEx.StackTrace);
                }
                innerEx = innerEx.InnerException;
                depth++;
            }
            
            sb.AppendLine("".PadRight(80, '-'));
            
            File.AppendAllText(logFilePath, sb.ToString());
        }
        catch
        {
            // Swallow if file logging fails to prevent cascading errors
        }
    }

    #endregion
}