using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace IronClad.Agent.Tests;

// Scan jobs run Windows Defender via PowerShell and cannot execute here, so this pins the structure
// that isolates failures: each iteration of the scan-job loop must be wrapped in try/catch, otherwise
// one malformed job aborts every remaining job on every cycle (upstream behavior).
public class Scan_Job_Isolation_Tests
{
    private static string Source([CallerFilePath] string here = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..",
            "NetLock RMM Agent Comm", "Windows", "Microsoft_Defender_Antivirus", "Scan_Jobs_Scheduler.cs")));

    private static ForEachStatementSyntax Scan_Job_Loop(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ForEachStatementSyntax>()
            .Single(f => f.Expression.ToString().Contains("program_data_microsoft_defender_antivirus_scan_jobs")
                         && f.Identifier.Text == "job"); // the execution loop (the "file" loop only cleans up old jobs)

    [Fact]
    public void Each_scan_job_iteration_is_isolated_by_try_catch()
    {
        var body = Assert.IsType<BlockSyntax>(Scan_Job_Loop(Source()).Statement);

        var guard = Assert.IsType<TryStatementSyntax>(Assert.Single(body.Statements));
        Assert.NotEmpty(guard.Catches);
    }

    [Fact]
    public void Legacy_loop_shape_is_detected()
    {
        const string legacy = """
            class C { void M() {
                foreach (var job in Directory.GetFiles(Application_Paths.program_data_microsoft_defender_antivirus_scan_jobs))
                {
                    string job_json = File.ReadAllText(job);
                    Run(job_json);
                }
            } }
            """;

        var body = (BlockSyntax)Scan_Job_Loop(legacy).Statement;
        Assert.IsNotType<TryStatementSyntax>(body.Statements.First());
    }
}
