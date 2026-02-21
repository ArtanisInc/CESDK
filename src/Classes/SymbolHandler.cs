using System;
using CESDK.Lua;

namespace CESDK.Classes
{
    public class SymbolHandlerException : CesdkException
    {
        public SymbolHandlerException(string message) : base(message) { }
        public SymbolHandlerException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class SymbolHandler
    {
        /// <summary>
        /// Reinitializes the symbol handler for the target process.
        /// Useful when new modules have been loaded into the target process.
        /// </summary>
        /// <param name="waitTillDone">If true (default), wait until reinitialization is complete</param>
        public static void Reinitialize(bool waitTillDone = true)
        {
            CESDK.Synchronize(() =>
            {
                try
                {
                    var lua = PluginContext.Lua;
                    lua.GetGlobal("reinitializeSymbolhandler");
                    if (!lua.IsFunction(-1))
                    {
                        lua.Pop(1);
                        throw new SymbolHandlerException("reinitializeSymbolhandler function not available");
                    }

                    // Push waitTillDone parameter
                    lua.PushBoolean(waitTillDone);

                    var result = lua.PCall(1, 0);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new SymbolHandlerException($"reinitializeSymbolhandler({waitTillDone}) call failed: {error}");
                    }
                }
                catch (Exception ex) when (ex is not SymbolHandlerException)
                {
                    throw new SymbolHandlerException($"Failed to reinitialize symbol handler (waitTillDone={waitTillDone})", ex);
                }
            });
        }

        /// <summary>
        /// Reinitializes the symbol handler for the Cheat Engine process itself.
        /// Useful when new modules have been loaded into CE process.
        /// </summary>
        /// <param name="waitTillDone">If true (default), wait until reinitialization is complete</param>
        public static void ReinitializeSelf(bool waitTillDone = true)
        {
            CESDK.Synchronize(() =>
            {
                try
                {
                    var lua = PluginContext.Lua;
                    lua.GetGlobal("reinitializeSelfSymbolhandler");
                    if (!lua.IsFunction(-1))
                    {
                        lua.Pop(1);
                        throw new SymbolHandlerException("reinitializeSelfSymbolhandler function not available");
                    }

                    // Push waitTillDone parameter
                    lua.PushBoolean(waitTillDone);

                    var result = lua.PCall(1, 0);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new SymbolHandlerException($"reinitializeSelfSymbolhandler({waitTillDone}) call failed: {error}");
                    }
                }
                catch (Exception ex) when (ex is not SymbolHandlerException)
                {
                    throw new SymbolHandlerException($"Failed to reinitialize self symbol handler (waitTillDone={waitTillDone})", ex);
                }
            });
        }

        /// <summary>
        /// Waits until the sections enumeration has completed.
        /// </summary>
        public static void WaitForSections()
        {
            try
            {
                var lua = PluginContext.Lua;
                lua.GetGlobal("waitForSections");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(1);
                    throw new SymbolHandlerException("waitForSections function not available");
                }

                var result = lua.PCall(0, 0);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    lua.Pop(1);
                    throw new SymbolHandlerException($"waitForSections() call failed: {error}");
                }
            }
            catch (Exception ex) when (ex is not SymbolHandlerException)
            {
                throw new SymbolHandlerException("Failed to wait for sections", ex);
            }
        }
    }
}
