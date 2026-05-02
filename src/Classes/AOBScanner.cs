using System;
using System.Collections.Generic;
using System.Globalization;
using CESDK.Lua;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class AobScanException : CesdkException
    {
        public AobScanException(string message)
            : base(message) { }

        public AobScanException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public static class AobScanner
    {
        private const ulong ManualRangeScanLimit = 32UL * 1024 * 1024;

        public static List<ulong> Scan(
            string pattern,
            string? protectionFlags = null,
            int alignmentType = 0,
            string? alignmentParam = null
        ) =>
            WrapException(() =>
            {
                var lua = PluginContext.Lua;
                var initialTop = lua.GetTop();

                try
                {
                    lua.GetGlobal("AOBScan");
                    if (!lua.IsFunction(-1))
                    {
                        lua.Pop(1);
                        throw new InvalidOperationException(
                            "AOBScan function not available in this CE version"
                        );
                    }

                    lua.PushString(pattern);
                    lua.PushString(protectionFlags ?? string.Empty);
                    lua.PushInteger(alignmentType);
                    lua.PushString(alignmentParam ?? string.Empty);

                    var result = lua.PCall(4, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"AOBScan() call failed: {error}");
                    }

                    return ProcessScanResults();
                }
                finally
                {
                    // Low-level Lua stack calls must always restore stack depth.
                    // Leaking stack entries can destabilize CE and/or cause later calls to crash.
                    lua.SetTop(initialTop);
                }
            });

        public static List<ulong> ScanRange(
            ulong startAddress,
            ulong stopAddress,
            string pattern,
            string? protectionFlags = null,
            int alignmentType = 0,
            string? alignmentParam = null
        ) =>
            WrapException(() =>
            {
                var lua = PluginContext.Lua;
                var initialTop = lua.GetTop();

                try
                {
                    lua.GetGlobal("AOBScanRegion");
                    if (!lua.IsFunction(-1))
                    {
                        lua.Pop(1);
                        return ScanRangeWithoutAobScanRegion(
                            startAddress,
                            stopAddress,
                            pattern,
                            protectionFlags,
                            alignmentType,
                            alignmentParam
                        );
                    }

                    lua.PushInteger(unchecked((long)startAddress));
                    lua.PushInteger(unchecked((long)stopAddress));
                    lua.PushString(pattern);
                    lua.PushString(protectionFlags ?? string.Empty);
                    lua.PushInteger(alignmentType);
                    lua.PushString(alignmentParam ?? string.Empty);

                    var result = lua.PCall(6, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException(
                            $"AOBScanRegion() call failed: {error}"
                        );
                    }

                    return ProcessScanResults();
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });

        private static List<ulong> ScanRangeWithoutAobScanRegion(
            ulong startAddress,
            ulong stopAddress,
            string pattern,
            string? protectionFlags,
            int alignmentType,
            string? alignmentParam
        )
        {
            var rangeSize = GetRangeSize(startAddress, stopAddress);
            if (rangeSize <= ManualRangeScanLimit)
                return ScanRangeByReadingMemory(startAddress, stopAddress, pattern);

            try
            {
                return ScanRangeWithMemScan(
                    startAddress,
                    stopAddress,
                    pattern,
                    protectionFlags,
                    alignmentType,
                    alignmentParam
                );
            }
            catch (Exception memScanError)
            {
                throw new InvalidOperationException(
                    "AOBScanRegion is not available and MemScan fallback failed. "
                        + $"Refusing manual readBytes scan over {rangeSize} bytes "
                        + $"(limit {ManualRangeScanLimit} bytes) to avoid unexpectedly slow scans. "
                        + "Narrow start/stop bounds or use memory_scan vtByteArray. "
                        + $"MemScan error: {memScanError.Message}"
                );
            }
        }

        private static List<ulong> ScanRangeWithMemScan(
            ulong startAddress,
            ulong stopAddress,
            string pattern,
            string? protectionFlags,
            int alignmentType,
            string? alignmentParam
        )
        {
            using var scanner = new MemScan();
            scanner.NewScan();
            scanner.FirstScan(
                new ScanParameters
                {
                    ScanOption = ScanOption.soExactValue,
                    VarType = VariableType.vtByteArray,
                    Input1 = pattern,
                    StartAddress = startAddress,
                    StopAddress = stopAddress,
                    ProtectionFlags = protectionFlags ?? string.Empty,
                    AlignmentType = (AlignmentType)alignmentType,
                    AlignmentParam = string.IsNullOrWhiteSpace(alignmentParam)
                        ? "1"
                        : alignmentParam,
                    IsHexadecimalInput = true,
                }
            );
            scanner.WaitTillDone();
            scanner.InitializeResults();

            var addresses = new List<ulong>();
            var count = scanner.GetResultCount();
            for (var i = 0; i < count; i++)
            {
                var addressText = scanner.GetResultAddress(i);
                if (TryParseAddressText(addressText, out var address))
                    addresses.Add(address);
            }

            return addresses;
        }

        private static List<ulong> ScanRangeByReadingMemory(
            ulong startAddress,
            ulong stopAddress,
            string pattern
        )
        {
            if (stopAddress < startAddress)
                throw new InvalidOperationException("stopAddress must be >= startAddress");

            var tokens = ParseAobPattern(pattern);
            if (tokens.Count == 0)
                return new List<ulong>();

            var addresses = new List<ulong>();
            const int maxChunkSize = 1024 * 1024;
            const ulong pageSize = 0x1000;
            var overlap = Math.Max(0, tokens.Count - 1);
            var current = startAddress;

            while (current <= stopAddress)
            {
                var remaining = stopAddress - current + 1;
                var chunkSize = (int)Math.Min((ulong)maxChunkSize, remaining);

                byte[] chunk;
                try
                {
                    chunk = MemoryAccess.ReadBytes(current, chunkSize);
                }
                catch
                {
                    if ((ulong)chunkSize > pageSize)
                    {
                        chunkSize = (int)Math.Min(pageSize, remaining);
                        try
                        {
                            chunk = MemoryAccess.ReadBytes(current, chunkSize);
                        }
                        catch
                        {
                            current = AdvanceAddress(current, (ulong)chunkSize, stopAddress);
                            continue;
                        }
                    }
                    else
                    {
                        current = AdvanceAddress(current, (ulong)chunkSize, stopAddress);
                        continue;
                    }
                }

                ScanChunk(addresses, current, chunk, tokens);

                if ((ulong)chunkSize >= remaining)
                    break;

                var step = (ulong)Math.Max(1, chunkSize - overlap);
                current = AdvanceAddress(current, step, stopAddress);
            }

            return addresses;
        }

        private static ulong GetRangeSize(ulong startAddress, ulong stopAddress)
        {
            if (stopAddress < startAddress)
                throw new InvalidOperationException("stopAddress must be >= startAddress");

            return stopAddress - startAddress + 1;
        }

        private static ulong AdvanceAddress(ulong current, ulong step, ulong stopAddress)
        {
            if (step == 0 || current > ulong.MaxValue - step)
                return stopAddress + 1;
            return current + step;
        }

        private static List<byte?> ParseAobPattern(string pattern)
        {
            var tokens = new List<byte?>();
            foreach (
                var token in pattern.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                if (token == "?" || token == "??")
                {
                    tokens.Add(null);
                    continue;
                }

                if (
                    !byte.TryParse(
                        token,
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var value
                    )
                )
                {
                    throw new InvalidOperationException($"Invalid AOB byte token: {token}");
                }

                tokens.Add(value);
            }

            return tokens;
        }

        private static void ScanChunk(
            List<ulong> addresses,
            ulong chunkBase,
            byte[] chunk,
            List<byte?> pattern
        )
        {
            if (pattern.Count > chunk.Length)
                return;

            for (int i = 0; i <= chunk.Length - pattern.Count; i++)
            {
                var matches = true;
                for (int j = 0; j < pattern.Count; j++)
                {
                    var expected = pattern[j];
                    if (expected.HasValue && chunk[i + j] != expected.Value)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    addresses.Add(chunkBase + (ulong)i);
            }
        }

        public static (string? signature, long offset) GetUniqueAOB(ulong address)
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;
                var initialTop = lua.GetTop();
                try
                {
                    lua.GetGlobal("getUniqueAOB");
                    if (!lua.IsFunction(-1))
                        throw new InvalidOperationException("getUniqueAOB function not available");

                    lua.PushInteger((long)address);
                    var result = lua.PCall(1, 2);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        throw new InvalidOperationException($"getUniqueAOB() call failed: {error}");
                    }

                    var sig = lua.ToString(-2);
                    var offset = lua.ToInteger(-1);
                    return (sig, offset);
                }
                finally
                {
                    lua.SetTop(initialTop);
                }
            });
        }

        public static ulong? ScanUnique(
            string pattern,
            string? protectionFlags = null,
            int alignmentType = 0,
            string? alignmentParam = null
        ) =>
            WrapException(() =>
                LuaUtils.CallLuaFunctionWithOptionalParams(
                    "AOBScanUnique",
                    "perform unique AOB scan",
                    LuaUtils.ParseAddressFromStack,
                    pattern,
                    protectionFlags ?? string.Empty,
                    alignmentType,
                    alignmentParam ?? string.Empty
                )
            );

        /// <summary>
        /// Performs an AOB scan within a specific module and returns the address only if there is a single match.
        /// </summary>
        /// <param name="pattern">The byte pattern to search for (e.g. "AA BB ?? CC")</param>
        /// <param name="moduleName">The name of the module to scan (e.g. "Game.exe")</param>
        /// <param name="protectionFlags">Memory protection filter (e.g. "+W-C")</param>
        /// <param name="alignmentType">Alignment type (0: None, 1: Divisible by, 2: Ends with)</param>
        /// <param name="alignmentParam">Alignment parameter (hex string)</param>
        /// <returns>The address of the match if unique, otherwise null</returns>
        public static ulong? ScanModuleUnique(
            string pattern,
            string moduleName,
            string? protectionFlags = null,
            int alignmentType = 0,
            string? alignmentParam = null
        )
 =>
            WrapException(() =>
                LuaUtils.CallLuaFunction(
                    "AOBScanModuleUnique",
                    "perform module unique AOB scan",
                    LuaUtils.ParseAddressFromStack,
                    moduleName, // CE Lua expects moduleName first
                    pattern,
                    protectionFlags ?? string.Empty,
                    alignmentType,
                    alignmentParam ?? string.Empty
                )
            );

        private static List<ulong> ProcessScanResults()
        {
            var addresses = new List<ulong>();
            var lua = PluginContext.Lua;

            if (!lua.IsUserData(-1))
            {
                lua.Pop(1);
                return addresses;
            }

            lua.GetField(-1, "Count");
            if (!lua.IsNumber(-1))
            {
                // Pop Count field first so the userdata is on top for destruction.
                lua.Pop(1);
                DestroyUserDataUnsafe(lua);
                lua.Pop(1);
                return addresses;
            }

            var count = lua.ToInteger(-1);
            lua.Pop(1);

            for (int i = 0; i < count; i++)
            {
                ProcessSingleResult(addresses, i, lua);
            }

            DestroyUserDataUnsafe(lua);
            lua.Pop(1);
            return addresses;
        }

        private static void DestroyUserDataUnsafe(LuaNative lua)
        {
            // AOBScan returns a StringList userdata. CE Lua docs use `results.destroy()`
            // (dot-call, no explicit self arg). Passing the userdata as an extra
            // parameter can crash CE with:
            //   "Exception processing message 0xc0000005 - unexpected parameters"
            // So we call the returned destroy function with 0 args.
            try
            {
                if (!lua.IsUserData(-1))
                    return;

                lua.GetField(-1, "destroy");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(1);
                    return;
                }

                var err = lua.PCall(0, 0);
                if (err != 0)
                {
                    // pop error message
                    lua.Pop(1);
                }
            }
            catch
            {
                // best-effort cleanup only
            }
        }

        private static void ProcessSingleResult(List<ulong> addresses, int index, LuaNative lua)
        {
            // Prefer StringList.getString(i) which matches CE Lua examples.
            // Some builds expose StringList userdata with a metatable that also supports
            // array-style indexing; keep that as a fallback.
            string? addressStr = null;

            try
            {
                lua.GetField(-1, "getString");
                if (lua.IsFunction(-1))
                {
                    lua.PushInteger(index);
                    var err = lua.PCall(1, 1);
                    if (err == 0 && lua.IsString(-1))
                        addressStr = lua.ToString(-1);
                    lua.Pop(1); // pop result (or error string)
                    if (!string.IsNullOrEmpty(addressStr))
                        goto Parse;
                    goto Fallback;
                }
                lua.Pop(1);
            }
            catch
            {
                // ignore and fall back to table indexing
                try
                {
                    lua.Pop(1);
                }
                catch
                { /* ignore */
                }
            }

            Fallback:
            lua.PushInteger(index);
            lua.GetTable(-2);
            if (lua.IsString(-1))
                addressStr = lua.ToString(-1);
            lua.Pop(1);

            Parse:
            var nonNullAddressStr = addressStr;
            if (nonNullAddressStr == null || nonNullAddressStr.Length == 0)
                return;

            var trimmed = nonNullAddressStr.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(2);

            if (
                TryParseAddressText(trimmed, out var address)
            )
            {
                addresses.Add(address);
            }
        }

        private static bool TryParseAddressText(string? addressText, out ulong address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(addressText))
                return false;

            var trimmed = addressText.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(2);

            return ulong.TryParse(
                trimmed,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out address
            );
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (InvalidOperationException ex)
            {
                throw new AobScanException(ex.Message, ex);
            }
        }
    }
}
