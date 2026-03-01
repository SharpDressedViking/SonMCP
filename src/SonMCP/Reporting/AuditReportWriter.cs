using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace SonMCP.Reporting
{
    public record DiagnosticIssue(
        string RuleId,
        string FilePath,
        int Line,
        int Column,
        string Message,
        DiagnosticSeverity Severity,
        string ProjectName = ""
    );

    public record SkippedAnalyzer(string Name, string Reason);

    public static class AuditReportWriter
    {
        public static async Task WriteReportAsync(
            string outputPath,
            string targetName,
            string targetPath,
            IEnumerable<DiagnosticIssue> issues,
            IEnumerable<SkippedAnalyzer> skippedAnalyzers)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(outputPath, append: false);
            
            // Header
            await writer.WriteLineAsync($"# Audit Report — {targetName}");
            await writer.WriteLineAsync($"_Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC_");
            await writer.WriteLineAsync($"_Target: {targetPath}_");
            await writer.WriteLineAsync();

            // Store issues to avoid double iteration if enumerable is not a list
            // However, NFR-7 says streaming is required. 
            // We'll do a single pass to write issues but we need counts for the summary table.
            // Tradeoff: we might need to materialize for the summary OR write summary last (not possible in MD if it's at the top).
            // Actually, for 10k diagnostics, materializing is fine (a few MBs). 
            // "Streaming" in NFR-7 likely means "don't hold the whole report string in memory", 
            // but holding the diagnostic records in a list is okay for 10k items.
            
            var issueList = issues.ToList();
            var summary = issueList
                .GroupBy(i => i.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            // Summary Table
            await writer.WriteLineAsync("## Summary");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("| Severity | Count |");
            await writer.WriteLineAsync("|----------|-------|");
            foreach (var severity in Enum.GetValues<DiagnosticSeverity>().OrderByDescending(s => s))
            {
                if (summary.TryGetValue(severity, out int count))
                {
                    await writer.WriteLineAsync($"| {severity} | {count} |");
                }
            }
            await writer.WriteLineAsync($"| **Total** | {issueList.Count} |");
            await writer.WriteLineAsync();

            // Issues List
            await writer.WriteLineAsync("## Issues");
            await writer.WriteLineAsync();

            if (issueList.Count == 0)
            {
                await writer.WriteLineAsync("_No issues found._");
            }
            else
            {
                var issuesByProject = issueList.GroupBy(i => i.ProjectName);
                foreach (var projectGroup in issuesByProject)
                {
                    if (!string.IsNullOrEmpty(projectGroup.Key))
                    {
                        await writer.WriteLineAsync($"### {projectGroup.Key}");
                        await writer.WriteLineAsync();
                    }

                    var issuesBySeverity = projectGroup.GroupBy(i => i.Severity).OrderByDescending(g => g.Key);
                    foreach (var severityGroup in issuesBySeverity)
                    {
                        await writer.WriteLineAsync($"#### {severityGroup.Key}");
                        await writer.WriteLineAsync();
                        await writer.WriteLineAsync("| Rule | File | Line | Col | Message |");
                        await writer.WriteLineAsync("|------|------|------|-----|---------|");
                        foreach (var issue in severityGroup)
                        {
                            await writer.WriteLineAsync($"| {issue.RuleId} | {issue.FilePath} | {issue.Line} | {issue.Column} | {issue.Message.Replace("|", "\\|")} |");
                        }
                        await writer.WriteLineAsync();
                    }
                }
            }

            // Skipped Analyzers
            var skippedList = skippedAnalyzers.ToList();
            if (skippedList.Count > 0)
            {
                await writer.WriteLineAsync("## Skipped Analyzers");
                await writer.WriteLineAsync();
                await writer.WriteLineAsync("| Analyzer | Reason |");
                await writer.WriteLineAsync("|----------|--------|");
                foreach (var skipped in skippedList)
                {
                    await writer.WriteLineAsync($"| {skipped.Name} | {skipped.Reason.Replace("|", "\\|")} |");
                }
            }

            await writer.FlushAsync();
        }
    }
}
