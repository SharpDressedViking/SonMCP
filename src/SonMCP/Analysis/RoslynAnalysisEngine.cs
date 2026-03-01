using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using SonMCP.Reporting;

namespace SonMCP.Analysis
{
    public class RoslynAnalysisEngine : IAnalysisEngine
    {
        private readonly ImmutableArray<DiagnosticAnalyzer> _analyzers;
        private readonly List<SkippedAnalyzer> _skippedAnalyzers = new();

        public RoslynAnalysisEngine()
        {
            _analyzers = LoadAnalyzers();
        }

        public bool CanAnalyze(string extension)
        {
            return extension == ".csproj" || extension == ".vbproj" || extension == ".sln";
        }

        public async Task<(IEnumerable<DiagnosticIssue> Issues, IEnumerable<SkippedAnalyzer> Skipped)> AnalyzeAsync(string path)
        {
            var issues = new List<DiagnosticIssue>();
            
            using var workspace = MSBuildWorkspace.Create();
            
            try
            {
                if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    var solution = await workspace.OpenSolutionAsync(path);
                    foreach (var project in solution.Projects)
                    {
                        var projectIssues = await AnalyzeProjectAsync(project);
                        issues.AddRange(projectIssues);
                    }
                }
                else
                {
                    var project = await workspace.OpenProjectAsync(path);
                    var projectIssues = await AnalyzeProjectAsync(project);
                    issues.AddRange(projectIssues);
                }
            }
            catch (Exception ex)
            {
                // In a multi-engine world, we might want to log this but not crash the whole tool
                // for now we'll let it bubble or return what we have
                throw new InvalidOperationException($"Roslyn analysis failed for {path}: {ex.Message}", ex);
            }

            return (issues, _skippedAnalyzers);
        }

        private async Task<IEnumerable<DiagnosticIssue>> AnalyzeProjectAsync(Project project)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) return Enumerable.Empty<DiagnosticIssue>();

            // Filter analyzers to only those that support this project's language,
            // e.g. don't run VB.NET analyzers against a C# compilation (causes AD0001 crashes).
            var languageAnalyzers = _analyzers
                .Where(a =>
                {
                    var attr = a.GetType().GetCustomAttribute<DiagnosticAnalyzerAttribute>();
                    // If no attribute or no languages specified, include the analyzer (best-effort).
                    if (attr == null || attr.Languages == null || attr.Languages.Length == 0)
                        return true;
                    return attr.Languages.Contains(project.Language);
                })
                .ToImmutableArray();

            if (languageAnalyzers.IsEmpty) return Enumerable.Empty<DiagnosticIssue>();

            var compilationWithAnalyzers = compilation.WithAnalyzers(languageAnalyzers);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            return diagnostics.Select(d => new DiagnosticIssue(
                d.Id,
                d.Location.SourceTree?.FilePath ?? "",
                d.Location.GetLineSpan().StartLinePosition.Line + 1,
                d.Location.GetLineSpan().StartLinePosition.Character + 1,
                d.GetMessage(),
                d.Severity,
                project.Name
            ));
        }

        private ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
        {
            var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Load C# Analyzers
            LoadAnalyzersFromAssembly(Path.Combine(appDir, "SonarAnalyzer.CSharp.dll"), "C#", analyzers);
            
            // Load VB Analyzers
            LoadAnalyzersFromAssembly(Path.Combine(appDir, "SonarAnalyzer.VisualBasic.dll"), "VB.NET", analyzers);

            return analyzers.ToImmutable();
        }

        private void LoadAnalyzersFromAssembly(string assemblyPath, string languageName, ImmutableArray<DiagnosticAnalyzer>.Builder builder)
        {
            if (!File.Exists(assemblyPath))
            {
                _skippedAnalyzers.Add(new SkippedAnalyzer($"SonarAnalyzer.{languageName}", $"Assembly not found: {assemblyPath}"));
                return;
            }

            try
            {
                var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                var analyzerTypes = assembly.GetExportedTypes()
                    .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));

                int count = 0;
                foreach (var type in analyzerTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is DiagnosticAnalyzer analyzer)
                        {
                            builder.Add(analyzer);
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _skippedAnalyzers.Add(new SkippedAnalyzer(type.Name, $"Initialization failed: {ex.Message}"));
                    }
                }
                
                if (count == 0)
                {
                    _skippedAnalyzers.Add(new SkippedAnalyzer($"SonarAnalyzer.{languageName}", "No analyzer types found in assembly."));
                }
            }
            catch (Exception ex)
            {
                _skippedAnalyzers.Add(new SkippedAnalyzer($"Assembly Loading ({languageName})", $"Failed to load analyzer assembly: {ex.Message}"));
            }
        }
    }
}
