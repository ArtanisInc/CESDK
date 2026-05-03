# CESDK - Cheat Engine SDK for C#

CESDK is the managed wrapper layer used by Cheat Engine MCP Server. It provides C#
classes over Cheat Engine's Lua-exposed functions and CE object model so plugin code can
work with typed .NET APIs instead of building Lua scripts for every operation.

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fhedgehogform%2FCESDK.svg?type=shield)](https://app.fossa.com/projects/git%2Bgithub.com%2Fhedgehogform%2FCESDK?ref=badge_shield)

## Role in this repository

```text
CeMCP MCP tools
  -> CESDK classes
  -> Cheat Engine Lua API
  -> Cheat Engine internals / target process
```

CESDK is not a standalone memory editor. It is a plugin-side SDK that expects to run
inside Cheat Engine with access to Cheat Engine's Lua state and CE object handles.

that CESDK wraps, such as `readBytes`, `AOBScan`, `createMemScan`, `debug_setBreakpoint`,
`enumMemoryRegions`, `disassemble`, and `setMemoryProtection`.

## Requirements

- Windows.
- Cheat Engine 7.6.2 or newer.
- .NET target compatibility with the host plugin.
- A Cheat Engine plugin runtime context.

The SDK project targets `netstandard2.0`; the host plugin targets `net10.0-windows`.

## Build

From the repository root:

```powershell
dotnet build CeMCP.sln -c Release
```

Or from this directory when working on CESDK only:

```powershell
dotnet build -c Release
```

## Core classes

| Class              | Purpose                                                                       |
| ------------------ | ----------------------------------------------------------------------------- |
| `LuaExecutor`      | Execute Lua and serialize return values.                                      |
| `LuaLogger`        | Logging helpers for CE/Lua integration.                                       |
| `CEObjectWrapper`  | Base lifecycle wrapper for CE object handles.                                 |
| `MemoryAccess`     | Read/write bytes, integers, floats, doubles, pointers, and strings.           |
| `MemoryAllocator`  | Allocate, free, and change memory protection.                                 |
| `MemoryRegions`    | Enumerate regions and read protection flags.                                  |
| `Process`          | Open and inspect target process state.                                        |
| `ModuleEnumerator` | Enumerate modules and memory regions.                                         |
| `AddressResolver`  | Resolve Cheat Engine address expressions.                                     |
| `SymbolManager`    | Symbol lookup, module size, pointer-size, and symbol refresh helpers.         |
| `SymbolHandler`    | Direct symbol handler refresh wrapper.                                        |
| `SymbolWaiter`     | Helpers for waiting on symbol readiness.                                      |
| `MemScan`          | Wrapper around CE `createMemScan` / `firstScan` / `nextScan`.                 |
| `FoundList`        | Wrapper for scan result lists.                                                |
| `AOBScanner`       | AOB scan helpers, bounded scan fallback logic, and signature helpers.         |
| `Disassembler`     | `disassemble`, instruction size, previous opcode, and function range helpers. |
| `AutoAssembler`    | Auto Assemble validation and execution helpers.                               |
| `AdvancedDebugger` | `debug_setBreakpoint` and debugger operations.                                |
| `Debugger`         | Higher-level debugger wrapper.                                                |
| `AddressList`      | Cheat table memory records.                                                   |
| `ThreadList`       | Thread enumeration helpers.                                                   |
| `Converter`        | String/hash conversion helpers.                                               |
| `Speedhack`        | CE speedhack state and control.                                               |

## Design principles

### Use CE-native APIs first

CESDK prefers Cheat Engine's documented Lua functions where they exist:

- `readBytes`, `writeBytes`, `readInteger`, `readQword`, `readPointer`.
- `AOBScan` and `AOBScanUnique`.
- `createMemScan` and `firstScan` for typed/bounded scans.
- `debug_setBreakpoint` and `debug_continueFromBreakpoint`.
- `enumMemoryRegions`, `getMemoryProtection`, `setMemoryProtection`.
- `disassemble`, `getInstructionSize`, `getPreviousOpcode`.
- `getAddress`, `getAddressSafe`, `getModuleSize`, `reinitializeSymbolhandler`.

### Make fallbacks explicit

Cheat Engine builds and contexts do not always expose the same helper functions. CESDK
therefore keeps fallback behavior explicit and bounded:

- Region-specific AOB helpers are used when available.
- If unavailable, bounded scans avoid silently switching to global process scans.
- Large manual scan fallbacks are guarded to avoid unexpectedly slow calls.
- Signature generation reports whether uniqueness was actually verified.

### Preserve 64-bit addresses

Many CE Lua values are numeric, but target addresses may exceed 32-bit ranges. CESDK
normalizes address parsing and serialization so 64-bit targets keep their full address
values.

### Manage CE object lifetimes

Cheat Engine exposes unmanaged object handles to Lua. CESDK wrappers implement
`IDisposable` and route object destruction through `CEObjectWrapper` so CE objects are not
double-destroyed or destroyed from the wrong context.

## Scanning behavior

CESDK exposes two related scan paths:

- `AOBScanner` for direct Array-of-Bytes scans and signature helpers.
- `MemScan` for Cheat Engine's typed scanning engine (`vtDword`, `vtString`,
  `vtByteArray`, and related scan options).

Important details:

- CE's documented `AOBScan` scans the currently opened process and does not accept
  `startAddress` / `stopAddress`.
- CE's documented `MemScan.firstScan` accepts `startAddress` and `stopAddress`.
- Some CE versions do not expose an `AOBScanRegion` function. CESDK accounts for that.
- Callers that require bounded behavior should use APIs that preserve bounds instead of
  relying on global `AOBScan` results.

## Debugging behavior

`AdvancedDebugger` wraps `debug_setBreakpoint` using CE's documented trigger and method
parameters:

- `bptExecute`, `bptAccess`, `bptWrite`.
- hardware debug-register, auto, exception, and INT3-style methods where available.

The MCP layer builds on this to provide non-blocking trace breakpoints. Breakpoint support
still depends on Cheat Engine's active debugger interface, target process permissions, and
hardware debug-register availability.

## Error handling

CESDK wraps low-level failures in SDK-specific exceptions where possible. Tool layers should
return structured JSON errors instead of leaking raw CE/Lua failures to MCP clients.

Expected failure cases include:

- Function not exposed by the current CE build.
- Target memory not readable/writable.
- Debugger not attachable to the process.
- Protection changes not supported exactly as requested by the OS.
- Symbol lookup not complete or unavailable.

## Development notes

- Prefer adding a capability check over assuming a CE function exists.
- Preserve Lua stack balance after every direct Lua call.
- Keep scan operations bounded when a caller supplied bounds.
- Dispose CE object wrappers deterministically.
- Verify changes against a live Cheat Engine runtime when touching Lua integration.

## License

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fhedgehogform%2FCESDK.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2Fhedgehogform%2FCESDK?ref=badge_large)
