using System;
using System.Security.Cryptography;
using System.Text;
using CESDK.Utils;

namespace CESDK.Classes
{
    public class ConverterException : CesdkException
    {
        public ConverterException(string message)
            : base(message) { }

        public ConverterException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public static class Converter
    {
        public static string StringToMD5(string value) =>
            WrapException(() =>
            {
                // Prefer a local implementation to avoid depending on Lua helpers.
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(bytes);
                    return ToHexLower(hash);
                }
            });

        public static string AnsiToUtf8(string text) =>
            WrapException(() =>
                CallStringConverter(text, "convert ANSI to UTF-8", "ansiToUTF8", "ansiToUtf8")
            );

        public static string Utf8ToAnsi(string text) =>
            WrapException(() =>
                CallStringConverter(text, "convert UTF-8 to ANSI", "UTF8ToAnsi", "utf8ToAnsi")
            );

        private static string CallStringConverter(
            string text,
            string operationName,
            params string[] functionNames
        )
        {
            if (functionNames.Length == 0)
                throw new ArgumentException("No function names provided", nameof(functionNames));

            InvalidOperationException? last = null;
            foreach (var functionName in functionNames)
            {
                try
                {
                    return LuaUtils.CallLuaFunction(
                        functionName,
                        operationName,
                        () => PluginContext.Lua.ToString(-1) ?? "",
                        text
                    );
                }
                catch (InvalidOperationException ex)
                {
                    last = ex;

                    if (IsMissingLuaFunctionError(ex))
                        continue;

                    throw;
                }
            }

            throw last ?? new InvalidOperationException("Conversion failed");
        }

        private static bool IsMissingLuaFunctionError(InvalidOperationException ex)
        {
            var msg = ex.Message ?? "";
            return msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("is not a function", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ToHexLower(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(
                    bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
                );
            return sb.ToString();
        }

        private static T WrapException<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (InvalidOperationException ex)
            {
                throw new ConverterException(ex.Message, ex);
            }
        }
    }
}
