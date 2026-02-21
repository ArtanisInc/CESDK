using System;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class MemoryAllocationException : CesdkException
    {
        public MemoryAllocationException(string message) : base(message) { }
        public MemoryAllocationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class MemoryAllocator
    {
        /// <summary>
        /// Allocates memory in the target process.
        /// </summary>
        /// <param name="size">Size in bytes to allocate</param>
        /// <param name="baseAddress">Optional preferred base address</param>
        /// <param name="protection">Optional memory protection flags (e.g., "rwx", "rw", "r")</param>
        /// <returns>Address of allocated memory, or 0 if allocation failed</returns>
        public static long AllocateMemory(int size, long? baseAddress = null, string? protection = null)
        {
            return WrapException(() =>
            {
                ProcessValidator.EnsureProcessOpen("Memory allocation");

                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("allocateMemory", "allocate memory", () =>
                {
                    lua.PushInteger(size);
                    int paramCount = 1;

                    if (baseAddress.HasValue && baseAddress.Value != 0)
                    {
                        lua.PushInteger(baseAddress.Value);
                        paramCount++;
                    }

                    if (!string.IsNullOrEmpty(protection))
                    {
                        if (paramCount == 1)
                        {
                            lua.PushNil();
                            paramCount++;
                        }
                        lua.PushString(protection!);
                        paramCount++;
                    }

                    var result = lua.PCall(paramCount, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"allocateMemory() call failed: {error}");
                    }

                    long address = 0;
                    if (lua.IsNumber(-1))
                    {
                        address = lua.ToInt64(-1);
                    }
                    lua.Pop(1);

                    return address;
                });
            });
        }

        /// <summary>
        /// Frees previously allocated memory in the target process.
        /// </summary>
        /// <param name="address">Address to free</param>
        /// <param name="size">Optional size to free</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool DeAllocate(long address, int? size = null)
        {
            return WrapException(() =>
            {
                ProcessValidator.EnsureProcessOpen("Memory deallocation");

                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("deAlloc", "deallocate memory", () =>
                {
                    lua.PushInteger(address);
                    int paramCount = 1;

                    if (size.HasValue)
                    {
                        lua.PushInteger(size.Value);
                        paramCount++;
                    }

                    var result = lua.PCall(paramCount, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"deAlloc() call failed: {error}");
                    }

                    bool success = lua.IsBoolean(-1) ? lua.ToBoolean(-1) : false;
                    lua.Pop(1);

                    return success;
                });
            });
        }

        /// <summary>
        /// Sets memory protection flags on a memory region.
        /// </summary>
        /// <param name="address">Base address of the region</param>
        /// <param name="size">Size of the region in bytes</param>
        /// <param name="readable">Allow read access</param>
        /// <param name="writable">Allow write access</param>
        /// <param name="executable">Allow execute access</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetMemoryProtection(long address, int size, bool readable, bool writable, bool executable)
        {
            return WrapException(() =>
            {
                ProcessValidator.EnsureProcessOpen("Set memory protection");

                var lua = PluginContext.Lua;

                return LuaUtils.CallLuaFunction("setMemoryProtection", "set memory protection", () =>
                {
                    lua.PushInteger(address);
                    lua.PushInteger(size);

                    lua.CreateTable(0, 3);
                    lua.PushBoolean(readable);
                    lua.SetField(-2, "R");
                    lua.PushBoolean(writable);
                    lua.SetField(-2, "W");
                    lua.PushBoolean(executable);
                    lua.SetField(-2, "X");

                    var result = lua.PCall(3, 1);
                    if (result != 0)
                    {
                        var error = lua.ToString(-1);
                        lua.Pop(1);
                        throw new InvalidOperationException($"setMemoryProtection() call failed: {error}");
                    }

                    bool success = lua.IsBoolean(-1) ? lua.ToBoolean(-1) : false;
                    lua.Pop(1);

                    return success;
                });
            });
        }

        /// <summary>
        /// Makes a memory region writable and executable (full access).
        /// </summary>
        /// <param name="address">Base address of the region</param>
        /// <param name="size">Size of the region in bytes</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool FullAccess(long address, int size) =>
            WrapException(() =>
            {
                ProcessValidator.EnsureProcessOpen("Set full memory access");
                LuaUtils.CallVoidLuaFunction("fullAccess", "set full memory access", address, size);
                return true;
            });

        private static T WrapException<T>(Func<T> operation)
        {
            try { return operation(); }
            catch (InvalidOperationException ex) { throw new MemoryAllocationException(ex.Message, ex); }
        }

        private static void WrapException(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) { throw new MemoryAllocationException(ex.Message, ex); }
        }
    }
}
