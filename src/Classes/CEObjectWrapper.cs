using System;
using CESDK.Lua;

namespace CESDK.Classes
{
    /// <summary>
    /// Base exception for all CESDK operations
    /// </summary>
    public class CesdkException : Exception
    {
        public CesdkException(string message)
            : base(message) { }

        public CesdkException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// Base class for wrapping Cheat Engine objects in C#
    /// </summary>
    public abstract class CEObjectWrapper : IDisposable
    {
        private static readonly System.Collections.Concurrent.ConcurrentQueue<IntPtr> _deferredDisposals = new();

        protected readonly LuaNative lua;
        protected IntPtr CEObject;
        protected bool Disposed { get; private set; }

        protected CEObjectWrapper()
        {
            lua = PluginContext.Lua;
            CEObject = IntPtr.Zero;
        }

        ~CEObjectWrapper()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (CEObject != IntPtr.Zero && !SuppressDestroy)
            {
                if (disposing)
                {
                    // Safe to call Lua on the current (main/gate-protected) thread
                    DestroyInternal(CEObject);
                }
                else
                {
                    // From finalizer - defer to main thread
                    _deferredDisposals.Enqueue(CEObject);
                }
            }

            CEObject = IntPtr.Zero;
            Disposed = true;
        }

        private void DestroyInternal(IntPtr obj)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(obj);
                lua.GetField(-1, "destroy");

                if (lua.IsFunction(-1))
                {
                    lua.PushValue(-2); // Push self
                    _ = lua.PCall(1, 0);
                }
            }
            catch
            {
                // Ignore errors during destruction
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        /// <summary>
        /// Processes any handles queued for disposal by finalizers.
        /// MUST be called from a thread where it's safe to use the Lua stack (typically inside the gate).
        /// </summary>
        public static void ProcessDeferredDisposals()
        {
            var lua = PluginContext.Lua;
            while (_deferredDisposals.TryDequeue(out var handle))
            {
                var initialTop = lua.GetTop();
                try
                {
                    lua.PushCEObject(handle);
                    lua.GetField(-1, "destroy");
                    if (lua.IsFunction(-1))
                    {
                        lua.PushValue(-2);
                        _ = lua.PCall(1, 0);
                    }
                }
                catch { }
                finally
                {
                    lua.SetTop(initialTop);
                }
            }
        }

        /// <summary>
        /// Pushes the CE object onto the Lua stack
        /// </summary>
        internal void PushCEObject()
        {
            if (Disposed)
                throw new ObjectDisposedException(GetType().Name);

            if (CEObject == IntPtr.Zero)
                throw new InvalidOperationException("CE object is not initialized");

            lua.PushCEObject(CEObject);
        }

        /// <summary>
        /// Sets the CE object from the current top of the Lua stack
        /// </summary>
        protected void SetCEObjectFromStack()
        {
            if (!lua.IsCEObject(-1))
                throw new InvalidOperationException("Top of stack is not a CE object");

            CEObject = lua.ToCEObject(-1);
        }

        #region Property Helpers

        protected int GetIntProperty(string name)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, name);
                return lua.ToInteger(-1);
            }
            catch
            {
                return 0;
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        protected long GetLongProperty(string name)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, name);
                return lua.ToInteger(-1);
            }
            catch
            {
                return 0;
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        protected string GetStringProperty(string name)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, name);
                return lua.ToString(-1) ?? "";
            }
            catch
            {
                return "";
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        protected bool GetBoolProperty(string name)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, name);
                return lua.ToBoolean(-1);
            }
            catch
            {
                return false;
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        protected void SetIntProperty(string name, int value)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.PushInteger(value);
                lua.SetField(-2, name);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        protected void SetStringProperty(string name, string value)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.PushString(value);
                lua.SetField(-2, name);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        protected void SetBoolProperty(string name, bool value)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.PushBoolean(value);
                lua.SetField(-2, name);
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        #endregion

        #region Method Helpers

        /// <summary>
        /// Calls a parameterless method on this CE object
        /// </summary>
        protected void CallMethod(string methodName)
        {
            var initialTop = lua.GetTop();
            try
            {
                lua.PushCEObject(CEObject);
                lua.GetField(-1, methodName);
                if (!lua.IsFunction(-1))
                    throw new InvalidOperationException($"{methodName} method not available");

                lua.PushValue(-2); // self
                var result = lua.PCall(1, 0);
                if (result != 0)
                {
                    var error = lua.ToString(-1);
                    throw new InvalidOperationException($"{methodName}() call failed: {error}");
                }
            }
            finally
            {
                lua.SetTop(initialTop);
            }
        }

        #endregion

        /// <summary>
        /// Whether this wrapper should skip destruction (e.g. for CE-owned objects like the main memscan,
        /// or FoundList objects that are owned by their parent MemScan).
        /// </summary>
        protected internal bool SuppressDestroy { get; set; }
    }
}
