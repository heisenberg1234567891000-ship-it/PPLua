using System;
using System.Diagnostics;
using System.IO;
using MoonSharp.Interpreter;
using PPLua;

// Escape and denial-of-service checks for the Lua sandbox, run outside the game.
static class Program
{
    static int failures;

    static int Main()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixture");

        Check("dangerous globals are absent", () =>
        {
            var sb = New(fixture);
            Expect(sb, "return io == nil and load == nil and loadstring == nil and dofile == nil and loadfile == nil", true);
            Expect(sb, "return debug == nil and dynamic == nil and string.dump == nil", true);
            Expect(sb, "return os.execute == nil and os.remove == nil and os.rename == nil and os.exit == nil and os.getenv == nil and os.tmpname == nil", true);
            Expect(sb, "return type(os.time()) == 'number' and type(os.clock()) == 'number'", true);
        });

        Check("globals list", () =>
        {
            var sb = New(fixture);
            var r = sb.Call(sb.Script.LoadString("local t = {} for k in pairs(_G) do t[#t+1] = k end table.sort(t) return table.concat(t, ' ')"));
            Console.WriteLine("    _G: " + r.String);
            r = sb.Call(sb.Script.LoadString("local t = {} for k in pairs(os) do t[#t+1] = k end table.sort(t) return table.concat(t, ' ')"));
            Console.WriteLine("    os: " + r.String);
        });

        Check("require stays inside the mod folder", () =>
        {
            var sb = New(fixture);
            Expect(sb, "return require('lib.util').answer", 42.0);
            ExpectError(sb, "return require('../secret')");
            ExpectError(sb, "return require('..secret')");
            ExpectError(sb, "return require('C:/Windows/win')");
            if (sb.ResolveFile("../secret.lua") != null) throw new Exception("ResolveFile let ../ through");
            if (sb.ResolveFile("C:\\Windows\\notepad.exe") != null) throw new Exception("ResolveFile let an absolute path through");
            if (sb.ResolveFile("a.png:stream") != null) throw new Exception("ResolveFile let an ADS path through");
            if (sb.ResolveFile("sprites/../lib/util.lua") == null) throw new Exception("ResolveFile rejected a valid path");
        });

        Check("infinite loop is stopped", () => ExpectBudget(New(fixture), "while true do end"));
        Check("pcall cannot swallow the stop", () =>
            ExpectBudget(New(fixture), "while true do pcall(function() while true do end end) end"));
        Check("loop inside a coroutine is stopped", () =>
            ExpectBudget(New(fixture), "local co = coroutine.create(function() while true do end end) coroutine.resume(co) return 'escaped'"));
        Check("runaway recursion stops the mod", () =>
        {
            var sb = New(fixture);
            try
            {
                sb.Call(sb.Script.LoadString("local function f() return 1 + f() end return f()"));
            }
            catch (Exception e)
            {
                Console.WriteLine($"    {e.GetType().Name}, dead={sb.Dead}");
            }
            if (!sb.Dead && !(sb.Script == null)) Console.WriteLine("    (raised a Lua error, sandbox still usable)");
        });

        Check("package table holds nothing dangerous", () =>
        {
            var sb = New(fixture);
            var r = sb.Call(sb.Script.LoadString("local t = {} for k, v in pairs(package) do t[#t+1] = k .. '=' .. type(v) end return table.concat(t, ' ')"));
            Console.WriteLine("    package: " + r.String);
        });
        Check("dead sandbox refuses calls", () =>
        {
            var sb = New(fixture);
            try { sb.Call(sb.Script.LoadString("while true do end")); } catch (LuaBudgetExceededException) { }
            if (!sb.Dead) throw new Exception("not dead");
            try { sb.Call(sb.Script.LoadString("return 1")); throw new Exception("call went through"); }
            catch (ScriptRuntimeException) { }
        });

        Check("string.rep is capped", () =>
        {
            var sb = New(fixture);
            ExpectError(sb, "return string.rep('x', 1e12)");
            ExpectError(sb, "return ('x'):rep(1e9)");
            Expect(sb, "return ('ab'):rep(3, '-')", "ab-ab-ab");
        });

        Check("locked metatables", () =>
        {
            var sb = New(fixture);
            var t = new Table(sb.Script);
            var meta = new Table(sb.Script);
            meta["__metatable"] = "locked";
            t.MetaTable = meta;
            sb.Script.Globals["obj"] = t;
            Expect(sb, "return getmetatable(obj)", "locked");
            ExpectError(sb, "setmetatable(obj, {})");
        });

        Check("nested Lua -> host -> Lua shares one budget", () =>
        {
            var sb = New(fixture, 200_000);
            sb.Script.Globals["host"] = DynValue.NewCallback((ctx, args) => sb.Call(args[0]));
            Expect(sb, "return host(function() return 7 end)", 7.0);
            ExpectBudget(sb, "host(function() while true do end end)");
        });

        Check("budget exception survives a catch-all in the host", () =>
        {
            var sb = New(fixture, 200_000);
            // Mimics the game wrapping a mod callback in try/catch(Exception).
            sb.Script.Globals["host"] = DynValue.NewCallback((ctx, args) =>
            {
                try { sb.Call(args[0]); } catch (Exception) { }
                return DynValue.Nil;
            });
            ExpectBudget(sb, "host(function() while true do end end) return 'kept running'");
        });

        Check("multi-assignment through C# __newindex keeps every value", () =>
        {
            new HarmonyLib.Harmony("pplua.tests").PatchAll(typeof(MultiAssignFix));
            var sb = New(fixture);
            var seen = new System.Collections.Generic.Dictionary<string, object>();
            var meta = new Table(sb.Script);
            meta["__newindex"] = DynValue.NewCallback((ctx, args) =>
            {
                seen[args[1].String] = args[2].ToObject();
                return DynValue.Nil;
            });
            sb.Script.Globals["obj"] = new Table(sb.Script) { MetaTable = meta };
            sb.Call(sb.Script.LoadString("obj.a, obj.b = 10, 20  obj.p, obj.q, obj.r, obj.s = 1, 2, 3, 4  local u; u, obj.m = 5, 6  obj.one = 'x'"));
            foreach (var (k, v) in new (string, object)[] { ("a", 10.0), ("b", 20.0), ("p", 1.0), ("q", 2.0), ("r", 3.0), ("s", 4.0), ("m", 6.0), ("one", "x") })
                if (!seen.TryGetValue(k, out object got) || !Equals(got, v)) throw new Exception($"obj.{k} = {got ?? "nil"}, expected {v}");
            Expect(sb, "local t = setmetatable({}, { __newindex = function(t, k, v) rawset(t, k, v * 2) end }) t.x, t.y = 1, 2 return t.x + t.y", 6.0);
        });

        Check("local without a value starts as nil", () =>
        {
            new HarmonyLib.Harmony("pplua.tests.locals").PatchAll(typeof(LocalWithoutValueFix));
            var sb = New(fixture);
            Expect(sb, @"
                local function normalize(dx, dy) local d = math.sqrt(dx * dx + dy * dy) return dx / d, dy / d, d end
                local out = {}
                for _, g in ipairs{ { x = 0 }, { x = 1 }, { x = 2 } } do
                  local dx, dy, step = normalize(g.x + 1, 4)
                  local hit, hx, hy
                  if step > 100 then hit, hx, hy = 'hit', 1, 2 end
                  out[#out + 1] = tostring(hit) .. ' ' .. tostring(hx) .. ' ' .. tostring(hy)
                end
                local p; local q, r
                return table.concat(out, ' / ') .. ' | ' .. tostring(p) .. ' ' .. tostring(q) .. ' ' .. tostring(r)",
                "nil nil nil / nil nil nil / nil nil nil | nil nil nil");
        });

        Check("every Lua file shipped in LuaMods compiles", () =>
        {
            string repo = AppContext.BaseDirectory;
            while (repo != null && !Directory.Exists(Path.Combine(repo, "LuaMods"))) repo = Path.GetDirectoryName(repo.TrimEnd(Path.DirectorySeparatorChar));
            if (repo == null) throw new Exception("LuaMods folder not found above " + AppContext.BaseDirectory);
            var sb = New(fixture);
            int count = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(repo, "LuaMods"), "*.lua", SearchOption.AllDirectories))
            {
                sb.Script.LoadString(File.ReadAllText(file), null, file.Substring(repo.Length));    // compiles, does not run
                count++;
            }
            if (count == 0) throw new Exception("no Lua files found");
            Console.WriteLine($"    {count} file(s)");
        });

        Check("normal code is fast enough", () =>
        {
            var sb = New(fixture);
            var sw = Stopwatch.StartNew();
            Expect(sb, "local s = 0 for i = 1, 1000000 do s = s + i % 7 end return s", 2999998.0);
            Console.WriteLine($"    1M-iteration loop: {sw.ElapsedMilliseconds} ms");
        });

        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures;
    }

    static Sandbox New(string root, long budget = Sandbox.DefaultBudget) =>
        new Sandbox(root, s => Console.WriteLine("    lua: " + s), budget);

    static void Check(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failures++;
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }

    static void Expect(Sandbox sb, string code, object expected)
    {
        DynValue r = sb.Call(sb.Script.LoadString(code));
        object actual = r.ToObject();
        if (!Equals(actual, expected)) throw new Exception($"`{code}` returned {actual ?? "nil"}, expected {expected}");
    }

    static void ExpectError(Sandbox sb, string code)
    {
        try
        {
            sb.Call(sb.Script.LoadString(code));
        }
        catch (InterpreterException e)
        {
            Console.WriteLine("    ok, error: " + (e.DecoratedMessage ?? e.Message));
            return;
        }
        throw new Exception($"`{code}` did not raise an error");
    }

    static void ExpectBudget(Sandbox sb, string code)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            sb.Call(sb.Script.LoadString(code));
        }
        catch (LuaBudgetExceededException)
        {
            Console.WriteLine($"    stopped after {sw.ElapsedMilliseconds} ms, dead={sb.Dead}");
            if (!sb.Dead) throw new Exception("sandbox not marked dead");
            return;
        }
        throw new Exception($"`{code}` was not stopped");
    }
}
