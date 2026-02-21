using System;
using System.Collections.Generic;
using CESDK.Utils;

namespace CESDK.Classes
{
    public enum BreakpointTrigger
    {
        Execute = 0,  // bptExecute - break on instruction execution
        Access = 1,   // bptAccess - break on memory read/write
        Write = 2     // bptWrite - break on memory write
    }

    public enum BreakpointMethod
    {
        Auto = -1,           // nil - Let CE choose the best method automatically
        DebugRegister = 0,   // bpmDebugRegister - Hardware debug registers (DR0-DR3, limited to 4 breakpoints but fast)
        Exception = 1,       // bpmException - Exception-based breakpoints (modifies page permissions)
        Int3 = 2            // bpmInt3 - Software breakpoint (inserts INT3/0xCC instruction, unlimited but modifies code)
    }

    public enum ContinueMethod
    {
        Run = 0,       // co_run - just continue
        StepInto = 1,  // co_stepinto - step into calls
        StepOver = 2   // co_stepover - step over calls
    }

    public class RegisterContext
    {
        // 32-bit registers
        public uint EAX { get; set; }
        public uint EBX { get; set; }
        public uint ECX { get; set; }
        public uint EDX { get; set; }
        public uint ESI { get; set; }
        public uint EDI { get; set; }
        public uint EBP { get; set; }
        public uint ESP { get; set; }
        public uint EIP { get; set; }
        public uint EFLAGS { get; set; }

        // 64-bit registers (0 if running in 32-bit)
        public ulong RAX { get; set; }
        public ulong RBX { get; set; }
        public ulong RCX { get; set; }
        public ulong RDX { get; set; }
        public ulong RSI { get; set; }
        public ulong RDI { get; set; }
        public ulong RBP { get; set; }
        public ulong RSP { get; set; }
        public ulong RIP { get; set; }
        public ulong R8 { get; set; }
        public ulong R9 { get; set; }
        public ulong R10 { get; set; }
        public ulong R11 { get; set; }
        public ulong R12 { get; set; }
        public ulong R13 { get; set; }
        public ulong R14 { get; set; }
        public ulong R15 { get; set; }
    }

    public class AdvancedDebuggerException : CesdkException
    {
        public AdvancedDebuggerException(string message) : base(message) { }
        public AdvancedDebuggerException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class AdvancedDebugger
    {
        /// <summary>
        /// Detaches the debugger from the target process.
        /// Optional cleanup - debugger automatically detaches when process closes.
        /// </summary>
        /// <returns>True if successfully detached</returns>
        public static bool StopDebugger() =>
            WrapException(() => LuaUtils.CallLuaFunction("detachIfPossible", "detach debugger", () => true));

        /// <summary>
        /// Starts the debugger for the currently opened process.
        /// This MUST be called before setting breakpoints for them to work.
        /// </summary>
        /// <param name="debugInterface">Debugger interface: 0=default, 1=Windows Debug, 2=VEH Debug, 3=Kernel Debug</param>
        /// <returns>True if debugger started successfully</returns>
        public static bool StartDebugger(int debugInterface = 0) =>
            WrapException(() =>
            {
                if (debugInterface != 0)
                    LuaUtils.CallVoidLuaFunction("debugProcess", "start debugger", debugInterface);
                else
                    LuaUtils.CallVoidLuaFunction("debugProcess", "start debugger");
                return true;
            });

        /// <summary>
        /// Sets a breakpoint at the specified address.
        /// </summary>
        /// <param name="address">Address to set breakpoint</param>
        /// <param name="size">Size for access/write breakpoints (ignored for execute)</param>
        /// <param name="trigger">Breakpoint trigger type</param>
        /// <param name="method">Breakpoint implementation method (Auto = let CE decide)</param>
        /// <param name="luaCallback">Optional Lua callback function to execute on breakpoint hit</param>
        /// <returns>True if successful</returns>
        public static bool SetBreakpoint(long address, int size = 1, BreakpointTrigger trigger = BreakpointTrigger.Execute, BreakpointMethod method = BreakpointMethod.Auto, string? luaCallback = null)
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("debug_setBreakpoint", "set breakpoint", () =>
                {
                    lua.PushInteger(address);
                    
                    if (trigger != BreakpointTrigger.Execute)
                        lua.PushInteger(size);
                    else
                        lua.PushNil();

                    lua.PushInteger((int)trigger);

                    if (method == BreakpointMethod.Auto)
                        lua.PushNil();
                    else
                        lua.PushInteger((int)method);

                    if (!string.IsNullOrEmpty(luaCallback))
                    {
                        if (lua.LoadString(luaCallback!) != 0)
                        {
                            var error = lua.ToString(-1);
                            lua.Pop(1);
                            throw new InvalidOperationException($"Invalid Lua callback: {error}");
                        }
                    }
                    else
                    {
                        lua.PushNil();
                    }

                    var result = lua.PCall(5, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"debug_setBreakpoint() failed: {error}");
                    }

                    bool success = true;
                    if (lua.IsBoolean(-1) && !lua.ToBoolean(-1))
                        success = false;
                    else if (lua.IsNumber(-1) && lua.ToInteger(-1) == 0)
                        success = false;

                    lua.Pop(1);
                    return success;
                });
            });
        }

        /// <summary>
        /// Removes a breakpoint at the specified address.
        /// </summary>
        /// <param name="address">Address of breakpoint to remove</param>
        /// <returns>True if successful</returns>
        public static bool RemoveBreakpoint(long address)
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("debug_removeBreakpoint", "remove breakpoint", () =>
                {
                    lua.PushInteger(address);

                    var result = lua.PCall(1, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"debug_removeBreakpoint() failed: {error}");
                    }

                    bool success = true;
                    if (lua.IsBoolean(-1) && !lua.ToBoolean(-1))
                        success = false;
                    else if (lua.IsNumber(-1) && lua.ToInteger(-1) == 0)
                        success = false;

                    lua.Pop(1);
                    return success;
                });
            });
        }

        /// <summary>
        /// Gets list of all breakpoint addresses.
        /// </summary>
        /// <returns>List of breakpoint addresses</returns>
        public static List<long> GetBreakpointList()
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;
                return LuaUtils.CallLuaFunction("debug_getBreakpointList", "get breakpoint list", () =>
                {
                    var breakpoints = new List<long>();
                    var result = lua.PCall(0, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"debug_getBreakpointList() failed: {error}");
                    }

                    if (lua.IsTable(-1))
                    {
                        lua.PushNil();
                        while (lua.Next(-2) != 0)
                        {
                            if (lua.IsNumber(-1))
                            {
                                breakpoints.Add(lua.ToInt64(-1));
                            }
                            else if (lua.IsString(-1))
                            {
                                var addrStr = lua.ToString(-1);
                                if (long.TryParse(addrStr.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out long addr))
                                    breakpoints.Add(addr);
                            }
                            lua.Pop(1);
                        }
                    }

                    lua.Pop(1);
                    return breakpoints;
                });
            });
        }

        /// <summary>
        /// Continues execution from a breakpoint.
        /// </summary>
        /// <param name="method">Continue method (Run, StepInto, StepOver)</param>
        /// <returns>True if successful</returns>
        public static bool ContinueFromBreakpoint(ContinueMethod method)
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;

                string methodStr = method switch
                {
                    ContinueMethod.Run => "co_run",
                    ContinueMethod.StepInto => "co_stepinto",
                    ContinueMethod.StepOver => "co_stepover",
                    _ => "co_run"
                };

                lua.GetGlobal(methodStr);
                LuaUtils.CallVoidLuaFunction("debug_continueFromBreakpoint", "continue from breakpoint", 1);
                return true;
            });
        }

        /// <summary>
        /// Gets the current register context.
        /// </summary>
        /// <param name="extraRegs">Include FP and XMM registers</param>
        /// <returns>Register context</returns>
        public static RegisterContext GetContext(bool extraRegs = false)
        {
            return WrapException(() =>
            {
                LuaUtils.CallVoidLuaFunction("debug_getContext", "get context", extraRegs);

                var context = new RegisterContext();

                // Read 32-bit registers
                context.EAX = GetRegisterValue("EAX");
                context.EBX = GetRegisterValue("EBX");
                context.ECX = GetRegisterValue("ECX");
                context.EDX = GetRegisterValue("EDX");
                context.ESI = GetRegisterValue("ESI");
                context.EDI = GetRegisterValue("EDI");
                context.EBP = GetRegisterValue("EBP");
                context.ESP = GetRegisterValue("ESP");
                context.EIP = GetRegisterValue("EIP");
                context.EFLAGS = GetRegisterValue("EFLAGS");

                // Try to read 64-bit registers
                context.RAX = GetRegisterValue64("RAX");
                context.RBX = GetRegisterValue64("RBX");
                context.RCX = GetRegisterValue64("RCX");
                context.RDX = GetRegisterValue64("RDX");
                context.RSI = GetRegisterValue64("RDI");
                context.RDI = GetRegisterValue64("RDI");
                context.RBP = GetRegisterValue64("RBP");
                context.RSP = GetRegisterValue64("RSP");
                context.RIP = GetRegisterValue64("RIP");
                context.R8 = GetRegisterValue64("R8");
                context.R9 = GetRegisterValue64("R9");
                context.R10 = GetRegisterValue64("R10");
                context.R11 = GetRegisterValue64("R11");
                context.R12 = GetRegisterValue64("R12");
                context.R13 = GetRegisterValue64("R13");
                context.R14 = GetRegisterValue64("R14");
                context.R15 = GetRegisterValue64("R15");

                return context;
            });
        }

        /// <summary>
        /// Sets a register value.
        /// </summary>
        /// <param name="registerName">Register name (e.g., "EAX", "RAX")</param>
        /// <param name="value">Value to set</param>
        public static void SetRegister(string registerName, long value)
        {
            WrapException(() =>
            {
                var lua = PluginContext.Lua;
                lua.PushInteger(value);
                lua.SetGlobal(registerName);

                LuaUtils.CallVoidLuaFunction("debug_setContext", "set context", false);
            });
        }

        /// <summary>
        /// Checks if debugger is currently debugging.
        /// </summary>
        public static bool IsDebugging() =>
            WrapException(() => LuaUtils.CallLuaFunction("debug_isDebugging", "check if debugging", 
                () => PluginContext.Lua.IsBoolean(-1) && PluginContext.Lua.ToBoolean(-1)));

        /// <summary>
        /// Checks if debugger is currently broken (halted on breakpoint).
        /// </summary>
        public static bool IsBroken() =>
            WrapException(() => LuaUtils.CallLuaFunction("debug_isBroken", "check if broken", 
                () => PluginContext.Lua.IsBoolean(-1) && PluginContext.Lua.ToBoolean(-1)));

        private static uint GetRegisterValue(string registerName)
        {
            var lua = PluginContext.Lua;
            lua.GetGlobal(registerName);
            uint value = 0;
            if (lua.IsNumber(-1))
            {
                value = (uint)lua.ToInteger(-1);
            }
            lua.Pop(1);
            return value;
        }

        private static ulong GetRegisterValue64(string registerName)
        {
            var lua = PluginContext.Lua;
            lua.GetGlobal(registerName);
            ulong value = 0;
            if (lua.IsNumber(-1))
            {
                value = (ulong)lua.ToInt64(-1);
            }
            lua.Pop(1);
            return value;
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try { return operation(); }
            catch (InvalidOperationException ex) { throw new AdvancedDebuggerException(ex.Message, ex); }
        }

        private static void WrapException(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) { throw new AdvancedDebuggerException(ex.Message, ex); }
        }
    }
}
