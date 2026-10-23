using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Loaders;

namespace PPLua
{
    /// <summary>
    /// Thrown when a script runs past its instruction budget. It is deliberately not an
    /// InterpreterException, so Lua's pcall/xpcall cannot catch it.
    /// </summary>
    public sealed class LuaBudgetExceededException : Exception
    {
        public LuaBudgetExceededException(long budget)
            : base($"script ran more than {budget:N0} instructions in one call (infinite loop?)") { }
    }

    /// <summary>
    /// One locked-down Lua state per mod. Present: base functions, string, table, math, bit32, coroutine,
    /// error handling, metatables, os.time/clock/date and json. Absent: io, os.execute/remove/exit,
    /// load/loadfile/dofile/loadstring, debug, dynamic, string.dump and all CLR interop. Anything else a mod
    /// can reach is a callback the host adds explicitly.
    /// </summary>
    public sealed class Sandbox
    {
        public const CoreModules Modules = CoreModules.Preset_SoftSandbox & ~CoreModules.Dynamic;
        public const long DefaultBudget = 20_000_000;
        public const int MaxStringLength = 16 * 1024 * 1024;
        public const int MaxSourceBytes = 1024 * 1024;
        public const int MaxDepth = 24;

        static readonly Regex ModuleName = new Regex(@"^[A-Za-z0-9_\-]+(\.[A-Za-z0-9_\-]+)*$");

        public readonly Script Script;
        public readonly string Root;
        readonly Watchdog watchdog;
        readonly Dictionary<string, DynValue> modules = new Dictionary<string, DynValue>();
        int depth;

        public bool Dead { get; private set; }
        public string DeathReason { get; private set; }

        static Sandbox()
        {
            Script.DefaultOptions.ScriptLoader = new DenyLoader();
        }

        public Sandbox(string root, Action<string> print, long budget = DefaultBudget)
        {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
            Script = new Script(Modules);
            Script.Options.ScriptLoader = new DenyLoader();
            Script.Options.DebugPrint = print;
            watchdog = new Watchdog(budget);
            Script.AttachDebugger(watchdog);

            Table str = Script.Globals.Get("string").Table;
            str.Set("dump", DynValue.Nil);
            str.Set("rep", DynValue.NewCallback(Rep, "rep"));
            Script.Globals["require"] = DynValue.NewCallback(Require, "require");
        }

        public bool InCall => depth > 0;

        /// <summary>Stops the mod for good; later calls do nothing.</summary>
        public void Kill(string reason)
        {
            if (Dead) return;
            Dead = true;
            DeathReason = reason;
        }

        /// <summary>
        /// Calls a Lua function under the instruction budget. Calls nested inside another call of this sandbox
        /// (Lua -> game -> Lua) share the outer budget. Throws InterpreterException for Lua errors and
        /// LuaBudgetExceededException when the budget runs out (the sandbox is then dead).
        /// </summary>
        public DynValue Call(DynValue fn, params DynValue[] args)
        {
            if (Dead) throw new ScriptRuntimeException("this mod has been stopped: " + DeathReason);
            if (fn == null || (fn.Type != DataType.Function && fn.Type != DataType.ClrFunction)) return DynValue.Nil;
            if (depth >= MaxDepth) throw new ScriptRuntimeException("too many nested calls between Lua and the game");
            if (depth == 0) watchdog.Reset();
            depth++;
            try
            {
                return Script.Call(fn, args);
            }
            catch (LuaBudgetExceededException e)
            {
                Kill(e.Message);
                throw;
            }
            catch (Exception e) when (!(e is InterpreterException))
            {
                // e.g. IndexOutOfRangeException from MoonSharp's fixed-size stacks on runaway recursion.
                // The interpreter state can't be trusted afterwards.
                Kill("interpreter failure (runaway recursion?): " + e.Message);
                throw;
            }
            finally
            {
                depth--;
            }
        }

        /// <summary>Compiles and runs a file from the mod folder.</summary>
        public DynValue RunFile(string relative)
        {
            string path = ResolveFile(relative);
            if (path == null || !File.Exists(path)) throw new FileNotFoundException("not found in the mod folder", relative);
            if (new FileInfo(path).Length > MaxSourceBytes) throw new InvalidDataException(relative + " is larger than 1 MB");
            DynValue chunk = Script.LoadString(File.ReadAllText(path), null, relative.Replace('\\', '/'));
            return Call(chunk);
        }

        /// <summary>Maps a mod-relative path to a full path inside the mod folder, or null if it would leave it.</summary>
        public string ResolveFile(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Length > 200) return null;
            if (relative.IndexOf(':') >= 0 || relative.IndexOf('\0') >= 0 || Path.IsPathRooted(relative)) return null;
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(Root, relative));
            }
            catch (Exception)
            {
                return null;
            }
            return full.StartsWith(Root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        DynValue Require(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string name = args.AsType(0, "require", DataType.String, false).String;
            if (!ModuleName.IsMatch(name)) throw new ScriptRuntimeException($"require: bad module name '{name}'");
            if (modules.TryGetValue(name, out DynValue cached)) return cached;

            string relative = name.Replace('.', '/') + ".lua";
            string path = ResolveFile(relative);
            if (path == null || !File.Exists(path)) throw new ScriptRuntimeException($"require: '{relative}' not found in the mod folder");
            if (new FileInfo(path).Length > MaxSourceBytes) throw new ScriptRuntimeException($"require: '{relative}' is larger than 1 MB");

            modules[name] = DynValue.True; // a module that requires itself gets true instead of looping
            DynValue chunk = Script.LoadString(File.ReadAllText(path), null, relative);
            DynValue result = Call(chunk);
            if (result.IsNil() || result.IsVoid()) result = DynValue.True;
            modules[name] = result;
            return result;
        }

        static DynValue Rep(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string s = args.AsType(0, "rep", DataType.String, false).String;
            double n = args.AsType(1, "rep", DataType.Number, false).Number;
            DynValue sepArg = args.AsType(2, "rep", DataType.String, true);
            string sep = sepArg.IsNil() ? "" : sepArg.String;
            if (n < 1 || (s.Length == 0 && sep.Length == 0)) return DynValue.NewString("");
            if ((double)(s.Length + sep.Length) * n > MaxStringLength) throw new ScriptRuntimeException("string.rep: result too large");

            int count = (int)n;
            var sb = new StringBuilder(s.Length * count + sep.Length * (count - 1));
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(s);
            }
            return DynValue.NewString(sb.ToString());
        }

        /// <summary>Counts executed instructions via MoonSharp's debugger hook and aborts past the budget.</summary>
        sealed class Watchdog : IDebugger
        {
            static readonly DebuggerAction Run = new DebuggerAction { Action = DebuggerAction.ActionType.Run };
            readonly long budget;
            long count;

            public Watchdog(long budget) { this.budget = budget; }

            public void Reset() { count = 0; }

            // Keeps throwing until the next Reset, so even a caller that swallows the exception
            // (the game wraps mod callbacks in catch-all blocks) cannot keep the script running.
            public bool IsPauseRequested()
            {
                if (++count > budget) throw new LuaBudgetExceededException(budget);
                return false;
            }

            public DebuggerAction GetAction(int ip, SourceRef sourceref) => Run;
            public DebuggerCaps GetDebuggerCaps() => DebuggerCaps.CanDebugSourceCode;
            public void SetDebugService(DebugService debugService) { }
            public void SetSourceCode(SourceCode sourceCode) { }
            public void SetByteCode(string[] byteCode) { }
            public bool SignalRuntimeException(ScriptRuntimeException ex) => false;
            public void SignalExecutionEnded() { }
            public void Update(WatchType watchType, IEnumerable<WatchItem> items) { }
            public List<DynamicExpression> GetWatchItems() => new List<DynamicExpression>();
            public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }
        }

        /// <summary>No file ever reaches MoonSharp's own loaders; require above is the only way in.</summary>
        sealed class DenyLoader : ScriptLoaderBase
        {
            public override object LoadFile(string file, Table globalContext) =>
                throw new ScriptRuntimeException("file loading is disabled");

            public override bool ScriptFileExists(string name) => false;
        }
    }
}
