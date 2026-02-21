using System;
using CESDK.Classes;

namespace CESDK.Utils
{
    /// <summary>
    /// Helper class to validate that a process is open before memory operations.
    /// Many Cheat Engine operations require a process to be opened first.
    /// </summary>
    public static class ProcessValidator
    {
        /// <summary>
        /// Ensures that a process is currently open. Throws InvalidOperationException if not.
        /// </summary>
        /// <param name="operationName">Name of the operation requiring an open process (for error message)</param>
        /// <exception cref="InvalidOperationException">Thrown when no process is open</exception>
        public static void EnsureProcessOpen(string operationName = "This operation")
        {
            var pid = Process.GetOpenedProcessID();
            if (pid == 0)
            {
                throw new InvalidOperationException(
                    $"{operationName} requires an open process. " +
                    "No process is currently opened. Use openProcess() or /api/process/open first.");
            }
        }
    }
}
