using System;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class DisassemblerException : CesdkException
    {
        public DisassemblerException(string message) : base(message) { }
        public DisassemblerException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Parsed result of a disassembled instruction from splitDisassembledString.
    /// </summary>
    public class DisassembledInstruction
    {
        public string Address { get; init; } = "";
        public string Bytes { get; init; } = "";
        public string Opcode { get; init; } = "";
        public string Extra { get; init; } = "";
    }

    public static class Disassembler
    {
        private static readonly LuaNative lua = PluginContext.Lua;

        public static string? Disassemble(ulong address, int maxSize = 512) =>
            WrapException(() => LuaUtils.CallLuaFunction("disassemble", $"disassemble at 0x{address:X}",
                () => PluginContext.Lua.ToString(-1), address, maxSize));

        public static int GetInstructionSize(ulong address) =>
            WrapException(() => LuaUtils.CallLuaFunction("getInstructionSize", $"get instruction size at 0x{address:X}",
                () => PluginContext.Lua.ToInteger(-1), address));

        public static string? GetComment(ulong address) =>
            WrapException(() => LuaUtils.CallLuaFunction("getComment", $"get comment at 0x{address:X}",
                () => PluginContext.Lua.ToString(-1), address));

        public static void SetComment(ulong address, string comment) =>
            WrapException(() => LuaUtils.CallVoidLuaFunction("setComment", $"set comment at 0x{address:X}", address, comment));

        /// <summary>
        /// Returns the address of the previous opcode (estimated guess).
        /// </summary>
        public static ulong GetPreviousOpcode(ulong address) =>
            WrapException(() => LuaUtils.CallLuaFunction("getPreviousOpcode", $"get previous opcode at 0x{address:X}",
                () => unchecked((ulong)lua.ToInt64(-1)), address));

        /// <summary>
        /// Splits a disassembled string into its components: address, bytes, opcode, extra.
        /// </summary>
        public static DisassembledInstruction SplitDisassembledString(string disassembledString)
        {
            var parsed = ParseCeDisassembly(disassembledString);
            if (parsed != null)
                return parsed;

            return WrapException(() =>
            {
                var initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("splitDisassembledString");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("splitDisassembledString function not available");

                    lua.PushString(disassembledString);
                    var result = lua.PCall(1, 4);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        throw new InvalidOperationException($"splitDisassembledString() failed: {error}");
                    }

                    var instruction = new DisassembledInstruction
                    {
                        Address = lua.ToString(-4) ?? "",
                        Bytes = lua.ToString(-3) ?? "",
                        Opcode = lua.ToString(-2) ?? "",
                        Extra = lua.ToString(-1) ?? ""
                    };
                    return instruction;
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        private static DisassembledInstruction? ParseCeDisassembly(string disassembledString)
        {
            if (string.IsNullOrWhiteSpace(disassembledString))
                return null;

            var parts = disassembledString.Split(
                " - ",
                3,
                StringSplitOptions.TrimEntries
            );
            if (parts.Length < 3)
                return null;

            return new DisassembledInstruction
            {
                Address = parts[0],
                Bytes = parts[1],
                Opcode = parts[2],
                Extra = "",
            };
        }

        /// <summary>
        /// Disassembles a hex byte string or byte table and returns the result.
        /// </summary>
        public static string? DisassembleBytes(string hexBytes, ulong address = 0) =>
            WrapException(() => LuaUtils.CallLuaFunction("disassembleBytes", $"disassemble bytes",
                () => lua.ToString(-1), hexBytes, address));

        /// <summary>
        /// Returns an estimated function range (start, end) for the given address.
        /// </summary>
        public static (ulong start, ulong end) GetFunctionRange(ulong address)
        {
            return WrapException(() =>
            {
                var initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("getFunctionRange");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("getFunctionRange function not available");

                    lua.PushInteger((long)address);
                    var result = lua.PCall(1, 2);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        throw new InvalidOperationException($"getFunctionRange() failed: {error}");
                    }

                    var start = unchecked((ulong)lua.ToInt64(-2));
                    var end = unchecked((ulong)lua.ToInt64(-1));
                    return (start, end);
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try { return operation(); }
            catch (InvalidOperationException ex) { throw new DisassemblerException(ex.Message, ex); }
        }

        private static void WrapException(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) { throw new DisassemblerException(ex.Message, ex); }
        }
    }
}
