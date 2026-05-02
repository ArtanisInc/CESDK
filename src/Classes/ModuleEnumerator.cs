using System;
using System.Collections.Generic;
using CESDK.Utils;
using CESDK.Lua;

namespace CESDK.Classes
{
    public class ModuleEnumerationException : CesdkException
    {
        public ModuleEnumerationException(string message) : base(message) { }
        public ModuleEnumerationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class ModuleInfo
    {
        public string Name { get; set; } = "";
        public long Address { get; set; }
        public int Size { get; set; }
        public bool Is64Bit { get; set; }
        public string PathToFile { get; set; } = "";
    }

    public class MemoryRegionInfo
    {
        public long BaseAddress { get; set; }
        public long AllocationBase { get; set; }
        public int AllocationProtect { get; set; }
        public long RegionSize { get; set; }
        public int State { get; set; }
        public int Protect { get; set; }
        public int Type { get; set; }
    }

    public static class ModuleEnumerator
    {
        /// <summary>
        /// Enumerates all modules loaded in the target process.
        /// </summary>
        /// <param name="processId">Optional process ID to enumerate (defaults to current attached process)</param>
        /// <returns>List of module information</returns>
        public static List<ModuleInfo> EnumModules(int? processId = null)
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;
                var modules = new List<ModuleInfo>();

                lua.GetGlobal("enumModules");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(1);
                    throw new InvalidOperationException("enumModules function not available");
                }

                // Push processId if provided
                int paramCount = 0;
                if (processId.HasValue)
                {
                    lua.PushInteger(processId.Value);
                    paramCount = 1;
                }

                var result = lua.PCall(paramCount, 1);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    lua.Pop(1);
                    throw new InvalidOperationException($"enumModules() call failed: {error}");
                }

                // Result is a table (array) of module tables
                if (!lua.IsTable(-1))
                {
                    lua.Pop(1);
                    return modules;
                }

                // Iterate through the table
                lua.PushNil(); // First key
                while (lua.Next(-2) != 0)
                {
                    // Stack: table, key, value (module table)
                    if (lua.IsTable(-1))
                    {
                        var module = new ModuleInfo();

                        // Get Name
                        lua.GetField(-1, "Name");
                        if (lua.IsString(-1))
                            module.Name = lua.ToString(-1) ?? "";
                        lua.Pop(1);

                        // Get Address
                        lua.GetField(-1, "Address");
                        if (lua.IsNumber(-1))
                            module.Address = lua.ToInt64(-1);
                        lua.Pop(1);

                        // Get Is64Bit
                        lua.GetField(-1, "Is64Bit");
                        if (lua.IsBoolean(-1))
                            module.Is64Bit = lua.ToBoolean(-1);
                        lua.Pop(1);

                        // Get Size
                        lua.GetField(-1, "Size");
                        if (lua.IsNumber(-1))
                            module.Size = (int)lua.ToInteger(-1);
                        lua.Pop(1);

                        // Get PathToFile
                        lua.GetField(-1, "PathToFile");
                        if (lua.IsString(-1))
                            module.PathToFile = lua.ToString(-1) ?? "";
                        lua.Pop(1);

                        modules.Add(module);
                    }

                    lua.Pop(1); // Pop value, keep key for next iteration
                }

                lua.Pop(1); // Pop the table
                return modules;
            });
        }

        /// <summary>
        /// Gets the size of a loaded module.
        /// </summary>
        /// <param name="moduleName">Name of the module</param>
        /// <returns>Size of the module in bytes, or 0 if not found</returns>
        public static long GetModuleSize(string moduleName)
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;

                lua.GetGlobal("getModuleSize");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(1);
                    throw new InvalidOperationException("getModuleSize function not available");
                }

                lua.PushString(moduleName);

                var result = lua.PCall(1, 1);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    lua.Pop(1);
                    throw new InvalidOperationException($"getModuleSize() call failed: {error}");
                }

                long size = 0;
                if (lua.IsNumber(-1))
                    size = lua.ToInt64(-1);

                lua.Pop(1);
                return size;
            });
        }

        /// <summary>
        /// Enumerates all memory regions in the target process.
        /// </summary>
        /// <returns>List of memory region information</returns>
        public static List<MemoryRegionInfo> EnumMemoryRegions()
        {
            return WrapException(() =>
            {
                var lua = PluginContext.Lua;
                var regions = new List<MemoryRegionInfo>();

                lua.GetGlobal("enumMemoryRegions");
                if (!lua.IsFunction(-1))
                {
                    lua.Pop(1);
                    throw new InvalidOperationException("enumMemoryRegions function not available");
                }

                var result = lua.PCall(0, 1);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    lua.Pop(1);
                    throw new InvalidOperationException($"enumMemoryRegions() call failed: {error}");
                }

                // Result is a table (array) of region tables
                if (!lua.IsTable(-1))
                {
                    lua.Pop(1);
                    return regions;
                }

                // Iterate through the table
                lua.PushNil(); // First key
                while (lua.Next(-2) != 0)
                {
                    // Stack: table, key, value (region table)
                    if (lua.IsTable(-1))
                    {
                        var region = new MemoryRegionInfo();

                        // Get BaseAddress
                        lua.GetField(-1, "BaseAddress");
                        if (lua.IsNumber(-1))
                            region.BaseAddress = lua.ToInt64(-1);
                        lua.Pop(1);

                        // Get AllocationBase
                        lua.GetField(-1, "AllocationBase");
                        if (lua.IsNumber(-1))
                            region.AllocationBase = lua.ToInt64(-1);
                        lua.Pop(1);

                        // Get AllocationProtect
                        lua.GetField(-1, "AllocationProtect");
                        if (lua.IsNumber(-1))
                            region.AllocationProtect = (int)lua.ToInteger(-1);
                        lua.Pop(1);

                        // Get RegionSize
                        lua.GetField(-1, "RegionSize");
                        if (lua.IsNumber(-1))
                            region.RegionSize = lua.ToInt64(-1);
                        lua.Pop(1);

                        // Get State
                        lua.GetField(-1, "State");
                        if (lua.IsNumber(-1))
                            region.State = (int)lua.ToInteger(-1);
                        lua.Pop(1);

                        // Get Protect
                        lua.GetField(-1, "Protect");
                        if (lua.IsNumber(-1))
                            region.Protect = (int)lua.ToInteger(-1);
                        lua.Pop(1);

                        // Get Type
                        lua.GetField(-1, "Type");
                        if (lua.IsNumber(-1))
                            region.Type = (int)lua.ToInteger(-1);
                        lua.Pop(1);

                        regions.Add(region);
                    }

                    lua.Pop(1); // Pop value, keep key for next iteration
                }

                lua.Pop(1); // Pop the table
                return regions;
            });
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (InvalidOperationException ex)
            {
                throw new ModuleEnumerationException(ex.Message, ex);
            }
        }
    }
}
