using System.Text;
using Sharq.Core.Editor.DesignImport;

namespace SusDesignImport;

/// <summary>
/// CLI for ARCH-DESIGN-IMPORT §7.1a.
/// Usage:
///   dotnet run --project Tools~/SusDesignImport -- design import &lt;file.json&gt; [--out dir] [--dry-run] [--downstream]
///   dotnet run --project Tools~/SusDesignImport -- design validate &lt;file.json&gt;
///   dotnet run --project Tools~/SusDesignImport -- design map --list
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        // Accept both "design import …" and bare "import …"
        var tokens = new Queue<string>(args);
        if (string.Equals(tokens.Peek(), "design", StringComparison.OrdinalIgnoreCase))
            tokens.Dequeue();

        if (tokens.Count == 0)
        {
            PrintHelp();
            return 1;
        }

        var cmd = tokens.Dequeue().ToLowerInvariant();
        try
        {
            return cmd switch
            {
                "import" => CmdImport(tokens),
                "validate" => CmdValidate(tokens),
                "map" => CmdMap(tokens),
                _ => Fail($"unknown command '{cmd}'")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SusDesignImport] ERROR: {ex.Message}");
            return 2;
        }
    }

    static int CmdImport(Queue<string> tokens)
    {
        if (tokens.Count == 0) return Fail("import requires <path-to-export.json>");
        var path = tokens.Dequeue();
        var opts = new ImportOptions();
        string? outDir = null;

        while (tokens.Count > 0)
        {
            var a = tokens.Dequeue();
            switch (a)
            {
                case "--out":
                    outDir = tokens.Count > 0 ? tokens.Dequeue() : null;
                    break;
                case "--dry-run":
                    opts.DryRun = true;
                    break;
                case "--emit-unknown":
                    opts.EmitUnknown = true;
                    break;
                case "--downstream":
                    opts.Downstream = true;
                    break;
                case "--alias-map":
                    opts.AliasMapPath = tokens.Count > 0 ? tokens.Dequeue() : "";
                    break;
                case "--diff":
                    // reserved — print USS to stdout (same as dry-run body)
                    opts.DryRun = true;
                    break;
                default:
                    return Fail($"unknown import flag '{a}'");
            }
        }

        opts.OutDir = outDir ?? Path.Combine(Directory.GetCurrentDirectory(), "SusDesign");
        var json = File.ReadAllText(path, Encoding.UTF8);
        var result = DesignImporter.Import(json, opts);

        foreach (var w in result.Warnings)
            Console.Error.WriteLine($"[warn] {w}");
        foreach (var e in result.Errors)
            Console.Error.WriteLine($"[error] {e}");

        if (!result.Ok)
            return 1;

        Console.WriteLine(result.Uss);
        if (opts.DryRun)
        {
            Console.WriteLine($"[SusDesignImport] dry-run OK — {result.Mapped.Count} mapped, {result.Skipped.Count} skipped");
            Console.WriteLine($"[SusDesignImport] input sha256={result.InputSha256}");
        }
        else
        {
            Console.WriteLine($"[SusDesignImport] wrote {Path.Combine(opts.OutDir, opts.UssFileName)}");
            Console.WriteLine($"[SusDesignImport] wrote {Path.Combine(opts.OutDir, opts.MetaFileName)}");
            Console.WriteLine($"[SusDesignImport] {result.Mapped.Count} mapped, {result.Skipped.Count} skipped");
        }
        return 0;
    }

    static int CmdValidate(Queue<string> tokens)
    {
        if (tokens.Count == 0) return Fail("validate requires <file>");
        var path = tokens.Dequeue();
        var opts = new ImportOptions();
        while (tokens.Count > 0)
        {
            var a = tokens.Dequeue();
            switch (a)
            {
                case "--emit-unknown": opts.EmitUnknown = true; break;
                case "--downstream": opts.Downstream = true; break;
                case "--alias-map":
                    opts.AliasMapPath = tokens.Count > 0 ? tokens.Dequeue() : "";
                    break;
                default: return Fail($"unknown validate flag '{a}'");
            }
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        var result = DesignImporter.Validate(json, opts);
        foreach (var w in result.Warnings)
            Console.WriteLine($"[warn] {w}");
        foreach (var e in result.Errors)
            Console.Error.WriteLine($"[error] {e}");

        if (result.Ok)
        {
            Console.WriteLine("[SusDesignImport] validate OK");
            return 0;
        }
        Console.Error.WriteLine("[SusDesignImport] validate FAILED");
        return 1;
    }

    static int CmdMap(Queue<string> tokens)
    {
        var opts = new ImportOptions();
        var list = true;
        while (tokens.Count > 0)
        {
            var a = tokens.Dequeue();
            switch (a)
            {
                case "--list": list = true; break;
                case "--downstream": opts.Downstream = true; break;
                case "--alias-map":
                    opts.AliasMapPath = tokens.Count > 0 ? tokens.Dequeue() : "";
                    break;
                default: return Fail($"unknown map flag '{a}'");
            }
        }
        if (!list) return Fail("map requires --list");
        Console.Write(DesignImporter.MapList(opts));
        return 0;
    }

    static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    static int Fail(string msg)
    {
        Console.Error.WriteLine($"[SusDesignImport] ERROR: {msg}");
        PrintHelp();
        return 1;
    }

    static void PrintHelp()
    {
        Console.WriteLine(
@"SusDesignImport — design tokens → override USS (ARCH-DESIGN-IMPORT §7.1a–b)

  design import <file.json> [--out <dir>] [--dry-run] [--downstream] [--emit-unknown] [--alias-map <path>]
  design validate <file.json> [--downstream] [--emit-unknown]
  design map --list [--downstream]

Does NOT patch design-tokens.uss. Writes imported-tokens.uss + .sus-design-meta.json under --out.
Modes (mobile/desktop) emit .breakpoint-sm|md|… blocks compatible with SusBreakpointService.
");
    }
}
