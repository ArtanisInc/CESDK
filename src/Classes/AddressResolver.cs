using System;
using System.Globalization;
using System.Text.RegularExpressions;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class AddressResolutionException : CesdkException
    {
        public AddressResolutionException(string message)
            : base(message) { }

        public AddressResolutionException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public static class AddressResolver
    {
        private static readonly Regex ModuleOffsetHexPrefixRegex = new(
            "(?i)([+-])0x([0-9a-f]+)",
            RegexOptions.Compiled
        );

        private static string NormalizeSymbolExpression(string symbolName)
        {
            // CE's symbol handler is permissive, but `module+0x123` is not
            // consistently accepted across versions. Normalize to `module+123`.
            // Note: this only affects +/- offset segments, not leading 0x numbers.
            return ModuleOffsetHexPrefixRegex.Replace(symbolName, "$1$2");
        }

        public static ulong GetAddress(string symbolName, bool searchLocal = false) =>
            WrapException(() =>
            {
                symbolName = NormalizeSymbolExpression(symbolName);
                var args = searchLocal
                    ? new object[] { symbolName, true }
                    : new object[] { symbolName };
                return LuaUtils.CallLuaFunction(
                    "getAddress",
                    $"resolve address for '{symbolName}'",
                    ParseRequiredAddress,
                    args
                );
            });

        private static ulong ParseRequiredAddress()
        {
            var lua = PluginContext.Lua;

            if (lua.IsNumber(-1))
            {
                return unchecked((ulong)lua.ToInt64(-1));
            }
            else if (lua.IsString(-1))
            {
                var addressStr = lua.ToString(-1);

                if (string.IsNullOrEmpty(addressStr))
                    throw new InvalidOperationException("Invalid address format returned: <empty>");

                var trimmed = addressStr.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(2);

                if (
                    ulong.TryParse(
                        trimmed,
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var hexAddress
                    )
                )
                {
                    return hexAddress;
                }
                else if (
                    ulong.TryParse(
                        trimmed,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var decAddress
                    )
                )
                {
                    return decAddress;
                }
                throw new InvalidOperationException(
                    $"Invalid address format returned: {addressStr}"
                );
            }
            else
            {
                throw new InvalidOperationException(
                    "Symbol not found or address could not be resolved"
                );
            }
        }

        public static ulong? GetAddressSafe(string symbolName, bool searchLocal = false) =>
            WrapException(() =>
            {
                symbolName = NormalizeSymbolExpression(symbolName);
                var args = searchLocal
                    ? new object[] { symbolName, true }
                    : new object[] { symbolName };
                return LuaUtils.CallLuaFunction(
                    "getAddressSafe",
                    $"safely resolve address for '{symbolName}'",
                    ParseOptionalAddress,
                    args
                );
            });

        private static ulong? ParseOptionalAddress()
        {
            var lua = PluginContext.Lua;

            if (lua.IsNumber(-1))
            {
                return unchecked((ulong)lua.ToInt64(-1));
            }
            else if (lua.IsString(-1))
            {
                var addressStr = lua.ToString(-1);
                if (string.IsNullOrEmpty(addressStr))
                    return null;

                var trimmed = addressStr.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(2);

                if (
                    ulong.TryParse(
                        trimmed,
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var hexAddress
                    )
                )
                {
                    return hexAddress;
                }
                else if (
                    ulong.TryParse(
                        trimmed,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var decAddress
                    )
                )
                {
                    return decAddress;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns a descriptive name for the given address (e.g. "module.exe+offset" or "symbol").
        /// </summary>
        /// <param name="address">Memory address to resolve</param>
        /// <param name="moduleNames">Whether to include module names in the output</param>
        /// <param name="symbols">Whether to include symbol names in the output</param>
        /// <param name="sections">Whether to include section names in the output</param>
        /// <returns>The resolved name string</returns>
        public static string GetNameFromAddress(
            ulong address,
            bool moduleNames = true,
            bool symbols = true,
            bool sections = false
        ) =>
            WrapException(() =>
                LuaUtils.CallLuaFunction(
                    "getNameFromAddress",
                    $"get name from address 0x{address:X}",
                    () => PluginContext.Lua.ToString(-1) ?? "",
                    (long)address,
                    moduleNames,
                    symbols,
                    sections
                )
            );

        /// <summary>
        /// Checks if the given address is within any loaded module.
        /// </summary>
        /// <param name="address">Address to check</param>
        public static bool InModule(ulong address) =>
            WrapException(() =>
                LuaUtils.CallLuaFunction(
                    "inModule",
                    $"check if address 0x{address:X} is in module",
                    () => PluginContext.Lua.ToBoolean(-1),
                    (long)address
                )
            );

        /// <summary>
        /// Checks if the given address is within a system module (e.g. ntdll.dll).
        /// </summary>
        /// <param name="address">Address to check</param>
        public static bool InSystemModule(ulong address) =>
            WrapException(() =>
                LuaUtils.CallLuaFunction(
                    "inSystemModule",
                    $"check if address 0x{address:X} is in system module",
                    () => PluginContext.Lua.ToBoolean(-1),
                    (long)address
                )
            );

        private static T WrapException<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (InvalidOperationException ex)
            {
                throw new AddressResolutionException(ex.Message, ex);
            }
        }
    }
}
