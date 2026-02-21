using System;
using System.Collections.Generic;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class AutoAssemblerException : CesdkException
    {
        public AutoAssemblerException(string message) : base(message) { }
        public AutoAssemblerException(string message, Exception innerException) : base(message, innerException) { }
    }

    public enum AssemblePreference
    {
        None = 0,
        Short = 1,
        Long = 2,
        Far = 3
    }

    public class AutoAssembleResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, long> DisableInfo { get; set; } = new();
    }

    public static class AutoAssembler
    {
        /// <summary>
        /// Assembles a single line of assembly code.
        /// </summary>
        /// <param name="instruction">Assembly instruction (e.g., "mov eax,ebx")</param>
        /// <param name="address">Optional address where this code will be located</param>
        /// <param name="preference">Assembly preference (None, Short, Long, Far)</param>
        /// <param name="skipRangeCheck">Skip range checks and assemble anyway</param>
        /// <returns>Byte array of assembled code</returns>
        public static byte[] Assemble(string instruction, long? address = null, AssemblePreference preference = AssemblePreference.None, bool skipRangeCheck = false)
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("assemble", "assemble instruction", () =>
                {
                    lua.PushString(instruction);
                    int paramCount = 1;

                    if (address.HasValue && address.Value != 0)
                    {
                        lua.PushInteger(address.Value);
                        paramCount++;
                    }

                    if (preference != AssemblePreference.None)
                    {
                        if (paramCount == 1)
                        {
                            lua.PushNil();
                            paramCount++;
                        }
                        lua.PushInteger((int)preference);
                        paramCount++;
                    }

                    if (skipRangeCheck)
                    {
                        while (paramCount < 3)
                        {
                            lua.PushNil();
                            paramCount++;
                        }
                        lua.PushBoolean(true);
                        paramCount++;
                    }

                    var result = lua.PCall(paramCount, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"assemble() call failed: {error}");
                    }

                    var bytes = new List<byte>();
                    if (lua.IsTable(-1))
                    {
                        lua.PushNil();
                        while (lua.Next(-2) != 0)
                        {
                            if (lua.IsNumber(-1))
                            {
                                bytes.Add((byte)lua.ToInteger(-1));
                            }
                            lua.Pop(1);
                        }
                    }

                    lua.Pop(1);
                    return bytes.ToArray();
                });
            });
        }

        /// <summary>
        /// Executes an auto assembler script.
        /// </summary>
        /// <param name="script">Auto assembler script text</param>
        /// <param name="targetSelf">If true, assemble into Cheat Engine process instead of target</param>
        /// <param name="executeDisableSection">If true, handles the [Disable] section</param>
        /// <returns>Result with success status and disable info</returns>
        public static AutoAssembleResult AutoAssemble(string script, bool targetSelf = false, bool executeDisableSection = false)
        {
            return WrapException(() =>
            {
                if (!targetSelf)
                {
                    ProcessValidator.EnsureProcessOpen("Auto assembler");
                }

                var lua = PluginContext.Lua;
                var result = new AutoAssembleResult();

                return LuaUtils.CallLuaFunction("autoAssemble", "auto assemble", () =>
                {
                    lua.PushString(script);
                    lua.PushBoolean(targetSelf);

                    if (executeDisableSection)
                    {
                        lua.CreateTable(0, 0);
                    }
                    else
                    {
                        lua.PushNil();
                    }

                    var callResult = lua.PCall(3, 2);
                    if (callResult != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"autoAssemble() call failed: {error}");
                    }

                    result.Success = lua.IsBoolean(-2) && lua.ToBoolean(-2);

                    if (result.Success && lua.IsTable(-1))
                    {
                        lua.PushNil();
                        while (lua.Next(-2) != 0)
                        {
                            if (lua.IsString(-2) && lua.IsNumber(-1))
                            {
                                var key = lua.ToString(-2) ?? "";
                                var value = lua.ToInt64(-1);
                                result.DisableInfo[key] = value;
                            }
                            lua.Pop(1);
                        }
                    }

                    lua.Pop(2);
                    return result;
                });
            });
        }

        /// <summary>
        /// Checks an auto assembler script for syntax errors without executing it.
        /// </summary>
        /// <param name="script">Auto assembler script text</param>
        /// <param name="enable">Check the [Enable] section if true, [Disable] section if false</param>
        /// <param name="targetSelf">If true, check for assembly into Cheat Engine process</param>
        /// <returns>True if valid, false with error message if invalid</returns>
        public static (bool Success, string? ErrorMessage) AutoAssembleCheck(string script, bool enable = true, bool targetSelf = false)
        {
            return WrapException(() =>
            {
                if (!targetSelf)
                {
                    ProcessValidator.EnsureProcessOpen("Auto assembler check");
                }

                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("autoAssembleCheck", "auto assemble check", () =>
                {
                    lua.PushString(script);
                    lua.PushBoolean(enable);
                    lua.PushBoolean(targetSelf);

                    var result = lua.PCall(3, 2);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"autoAssembleCheck() call failed: {error}");
                    }

                    bool success = lua.IsBoolean(-2) && lua.ToBoolean(-2);
                    string? errorMessage = null;
                    if (!success && lua.IsString(-1))
                    {
                        errorMessage = lua.ToString(-1);
                    }

                    lua.Pop(2);
                    return (success, errorMessage);
                });
            });
        }

        /// <summary>
        /// Generates an auto assembler script for hooking an API function.
        /// </summary>
        /// <param name="address">Address to hook (function entry point)</param>
        /// <param name="addressToJumpTo">Address of your hook handler code</param>
        /// <param name="addressToGetNewCallAddress">Optional: Where to store the original function address</param>
        /// <param name="ext">Optional: Extension (e.g., file extension)</param>
        /// <param name="targetSelf">If true, generate hook for CE process</param>
        /// <returns>Generated auto assembler script</returns>
        public static string GenerateAPIHookScript(string address, string addressToJumpTo, string? addressToGetNewCallAddress = null, string? ext = null, bool targetSelf = false)
        {
            return WrapException(() =>
            {
                if (!targetSelf)
                {
                    ProcessValidator.EnsureProcessOpen("Generate API hook script");
                }

                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("generateAPIHookScript", "generate API hook script", () =>
                {
                    lua.PushString(address);
                    lua.PushString(addressToJumpTo);
                    int paramCount = 2;

                    if (!string.IsNullOrEmpty(addressToGetNewCallAddress))
                    {
                        lua.PushString(addressToGetNewCallAddress!);
                        paramCount++;
                    }

                    if (!string.IsNullOrEmpty(ext))
                    {
                        if (paramCount == 2)
                        {
                            lua.PushNil();
                            paramCount++;
                        }
                        lua.PushString(ext!);
                        paramCount++;
                    }

                    if (targetSelf)
                    {
                        while (paramCount < 4)
                        {
                            lua.PushNil();
                            paramCount++;
                        }
                        lua.PushBoolean(true);
                        paramCount++;
                    }

                    var result = lua.PCall(paramCount, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"generateAPIHookScript() call failed: {error}");
                    }

                    string script = "";
                    if (lua.IsString(-1))
                    {
                        script = lua.ToString(-1) ?? "";
                    }

                    lua.Pop(1);
                    return script;
                });
            });
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try { return operation(); }
            catch (InvalidOperationException ex) { throw new AutoAssemblerException(ex.Message, ex); }
        }

        private static void WrapException(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) { throw new AutoAssemblerException(ex.Message, ex); }
        }
    }
}
