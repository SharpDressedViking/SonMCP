using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SonMCP.Reporting;

namespace SonMCP.Analysis
{
    public class PythonAnalysisEngine : IAnalysisEngine
    {
        private readonly ILogger<PythonAnalysisEngine> _logger;
        private bool _autoInstallAttempted = false;

        public PythonAnalysisEngine(ILogger<PythonAnalysisEngine> logger)
        {
            _logger = logger;
        }

        public bool CanAnalyze(string extension)
        {
            return extension == ".py";
        }

        public async Task<(IEnumerable<DiagnosticIssue> Issues, IEnumerable<SkippedAnalyzer> Skipped)> AnalyzeAsync(string path)
        {
            var issues = new List<DiagnosticIssue>();
            var skipped = new List<SkippedAnalyzer>();

            if (!await EnsureRuffInstalled())
            {
                throw new InvalidOperationException("Ruff tool for Python static analysis is not installed and auto-installation failed. Please install it manually: 'pip install ruff'");
            }

            try
            {
                var targetPath = Path.GetFullPath(path);
                var workingDir = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;

                var startInfo = new ProcessStartInfo
                {
                    FileName = "ruff",
                    Arguments = $"check \"{targetPath}\" --output-format json --quiet",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDir
                };

                using var process = Process.Start(startInfo);
                if (process == null) throw new InvalidOperationException("Failed to start Ruff process.");

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();
                var output = await outputTask;
                var error = await errorTask;

                if (!string.IsNullOrWhiteSpace(output))
                {
                    var ruffResults = JsonSerializer.Deserialize<List<RuffIssue>>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (ruffResults != null)
                    {
                        foreach (var ri in ruffResults)
                        {
                            issues.Add(new DiagnosticIssue(
                                ri.Code,
                                ri.Filename,
                                ri.Location.Row,
                                ri.Location.Column,
                                ri.Message,
                                MapSeverity(ri.Level),
                                "Ruff"
                            ));
                        }
                    }
                }
                else if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogWarning("Ruff exited with code {ExitCode}: {Error}", process.ExitCode, error);
                    skipped.Add(new SkippedAnalyzer("Ruff", $"Ruff failed with exit code {process.ExitCode}: {error}"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ruff analysis failed for {Path}", path);
                skipped.Add(new SkippedAnalyzer("Ruff", ex.Message));
            }

            return (issues, skipped);
        }

        private async Task<bool> EnsureRuffInstalled()
        {
            if (IsCommandAvailable("ruff")) return true;

            if (_autoInstallAttempted) return false;
            _autoInstallAttempted = true;

            _logger.LogInformation("Ruff not found. Attempting auto-installation...");
            
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "pip",
                    Arguments = "install ruff",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return IsCommandAvailable("ruff");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-installation of Ruff failed.");
            }

            return false;
        }

        private bool IsCommandAvailable(string command)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "where", // Windows specific
                    Arguments = command,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static DiagnosticSeverity MapSeverity(string? level)
        {
            return level?.ToLowerInvariant() switch
            {
                "error" => DiagnosticSeverity.Error,
                "warning" => DiagnosticSeverity.Warning,
                "note" => DiagnosticSeverity.Info,
                _ => DiagnosticSeverity.Warning
            };
        }

        private sealed record RuffIssue(
            string Code,
            string Message,
            string Filename,
            string Level,
            RuffLocation Location
        );

        private sealed record RuffLocation(int Row, int Column);
    }
}
