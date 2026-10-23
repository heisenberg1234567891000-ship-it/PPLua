using System.Collections.Generic;
using HarmonyLib;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;
using MoonSharp.Interpreter.Tree;
using MoonSharp.Interpreter.Tree.Expressions;
using MoonSharp.Interpreter.Tree.Statements;

namespace PPLua
{
    /// <summary>
    /// MoonSharp 2.0 bug: when an assignment goes through a __newindex metamethod, ExecIndexSet pops the top
    /// of the value stack and leaves the metamethod's return value in its place. With one target that slot
    /// is the value being stored and nothing is lost, but in `a.x, a.y = 1, 2` it is the last value of the
    /// statement, so the final field receives garbage. Here the handler is called on the side instead (C#
    /// handlers directly, Lua ones through a nested Processor.Call, as C# callbacks call Lua) and the stack
    /// is left exactly as a plain table store leaves it. A __newindex that yields is not supported.
    /// </summary>
    [HarmonyPatch(typeof(Processor), "ExecIndexSet")]
    public static class MultiAssignFix
    {
        static bool Prefix(Processor __instance, Instruction i, int instructionPtr, ref int __result)
        {
            if (i.OpCode != OpCode.IndexSet && i.OpCode != OpCode.IndexSetN) return true;

            var stack = __instance.m_ValueStack;
            DynValue table = stack.Peek(i.Value == null ? 1 : 0).ToScalar();
            if (table.Type != DataType.Table) return true;
            DynValue key = (i.Value ?? stack.Peek(0)).ToScalar();
            if (!table.Table.Get(key).IsNil()) return true;
            DynValue handler = __instance.GetMetamethodRaw(table, "__newindex");
            if (handler == null || (handler.Type != DataType.ClrFunction && handler.Type != DataType.Function)) return true;

            if (i.Value == null) stack.Pop();
            stack.Pop();
            DynValue value = __instance.GetStoreValue(i);
            var args = new[] { table, key, value };
            if (handler.Type == DataType.ClrFunction)
                handler.Callback.Invoke(new ScriptExecutionContext(__instance, handler.Callback, i.SourceCodeRef), args);
            else
                __instance.Call(handler, args);
            __result = instructionPtr;
            return false;
        }
    }

    /// <summary>
    /// MoonSharp 2.0 bug: `local a, b` with no `=` compiles to an assignment from values that were never
    /// pushed, so the new locals pick up whatever was left on the value stack instead of nil (in a loop, a
    /// raycast hit from somewhere else, say). Such a declaration gets an explicit `= nil`, which assigns nil to
    /// every name exactly as Lua does. Compile-time, so it must be patched in before any script is loaded.
    /// </summary>
    [HarmonyPatch(typeof(AssignmentStatement), MethodType.Constructor, typeof(ScriptLoadingContext), typeof(Token))]
    public static class LocalWithoutValueFix
    {
        static void Postfix(ScriptLoadingContext lcontext, List<Expression> ___m_RValues)
        {
            if (___m_RValues.Count == 0) ___m_RValues.Add(new LiteralExpression(lcontext, DynValue.Nil));
        }
    }
}
