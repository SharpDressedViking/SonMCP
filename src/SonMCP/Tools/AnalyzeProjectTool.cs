using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using SonMCP.Analysis;
using SonMCP.Reporting;

namespace SonMCP.Tools
{
    [McpServerToolType]
    public class AnalyzeProjectTool
    {
        private readonly AnalysisEngineFactory _engineFactory;
        private readonly ILogger<AnalyzeProjectTool> _logger;

        public AnalyzeProjectTool(
            AnalysisEngineFactory engineFactory,
            ILogger<AnalyzeProjectTool> logger)
        {
            _engineFactory = engineFactory;
            _logger = logger;
        }

        [McpServerTool]
        [Description("Analyzes a project, source file, or directory using static analysis tools and generates a Markdown report. Supports C#, VB.NET, Python, and JS/TS.")]
        public async Task<object> AnalyzeProject(
            [Description("Absolute or relative path to the target (.sln, .csproj, .vbproj, .py, .js, .ts or a directory).")] string projectPath,
            [Description("Optional root path for the report. If omitted, uses the directory of the target.")] string? workspaceRoot = null,
            [Description("Optional language hint ('python', 'javascript', 'typescript') when analyzing a directory.")] string? language = null)
        {
            try
            {
                // 1. Validation
                if (!File.Exists(projectPath) && !Directory.Exists(projectPath))
                {
                    return new { status = "error", error = $"Path not found: {projectPath}" };
                }

                _logger.LogInformation("Starting analysis of {Path}", projectPath);

                // 2. Resolve Report Path
                string targetDir;
                if (Directory.Exists(projectPath))
                {
                    targetDir = Path.GetFullPath(projectPath);
                }
                else
                {
                    targetDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Directory.GetCurrentDirectory();
                }

                var auditDir = !string.IsNullOrWhiteSpace(workspaceRoot) 
                    ? Path.Combine(workspaceRoot, "PROGRESS", "AUDIT")
                    : Path.Combine(targetDir, "PROGRESS", "AUDIT");

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var targetName = Path.GetFileNameWithoutExtension(projectPath);
                if (string.IsNullOrEmpty(targetName)) targetName = "project";
                
                var reportFileName = $"{timestamp}_{targetName}.md";
                var reportPath = Path.Combine(auditDir, reportFileName);

                // 3. Engine Resolution
                // If it's a directory and no language provided, we might need to guess or use a default
                // For now, if directory, we prioritize Python/JS engines if hinted, otherwise let factory handle it.
                var engine = _engineFactory.GetEngine(projectPath, language);

                // 4. Execution
                var (issues, skipped) = await engine.AnalyzeAsync(projectPath);
                
                // 5. Report Writing
                await AuditReportWriter.WriteReportAsync(
                    reportPath,
                    targetName,
                    Path.GetFullPath(projectPath),
                    issues,
                    skipped
                );

                var issueList = issues.AsList();
                var summary = issueList
                    .GroupBy(i => i.Severity.ToString())
                    .ToDictionary(g => g.Key, g => g.Count());

                _logger.LogInformation("Analysis complete. Report written to {Path}", reportPath);

                return new
                {
                    status = "success",
                    report_path = Path.GetFullPath(reportPath),
                    summary
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Analysis failed for {Path}", projectPath);
                return new { status = "error", error = ex.Message };
            }
        }
    }

    internal static class IEnumerableExtensions
    {
        public static List<T> AsList<T>(this IEnumerable<T> source)
        {
            return source as List<T> ?? source.ToList();
        }
    }
}
