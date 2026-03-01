using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SonMCP.Reporting;

namespace SonMCP.Analysis
{
    public class JavascriptAnalysisEngine : IAnalysisEngine
    {
        private readonly ILogger<JavascriptAnalysisEngine> _logger;
        private bool _autoInstallAttempted = false;

        public JavascriptAnalysisEngine(ILogger<JavascriptAnalysisEngine> logger)
        {
            _logger = logger;
        }

        public bool CanAnalyze(string extension)
        {
            string[] supported = { ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs" };
            return supported.Contains(extension);
        }

        public async Task<(IEnumerable<DiagnosticIssue> Issues, IEnumerable<SkippedAnalyzer> Skipped)> AnalyzeAsync(string path)
        {
            var issues = new List<DiagnosticIssue>();
            var skipped = new List<SkippedAnalyzer>();

            try
            {
                var targetPath = Path.GetFullPath(path);
                var workingDir = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;

                if (!await EnsureEslintDepsInstalled(workingDir))
                {
                    skipped.Add(new SkippedAnalyzer("ESLint", "ESLint or its required dependencies (@eslint/js, typescript-eslint, globals) are not installed and auto-installation failed."));
                    return (issues, skipped);
                }
                
                string? tempConfigPath = null;
                
                // Check for existing ESLint 9+ config
                string[] configFiles = { "eslint.config.js", "eslint.config.mjs", "eslint.config.cjs" };
                if (!configFiles.Any(f => File.Exists(Path.Combine(workingDir, f))))
                {
                    _logger.LogInformation("No ESLint configuration found in {Dir}. Using fallback configuration.", workingDir);
                    
                    var fallbackSource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefaultConfigs", "eslint.config.mjs");
                    // Fallback to source tree location if running during development
                    if (!File.Exists(fallbackSource))
                    {
                         fallbackSource = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "DefaultConfigs", "eslint.config.mjs"));
                    }

                    if (File.Exists(fallbackSource))
                    {
                        tempConfigPath = Path.Combine(workingDir, "eslint.config.mjs");
                        File.Copy(fallbackSource, tempConfigPath, true);
                        skipped.Add(new SkippedAnalyzer("ESLint", "No ESLint configuration found. Using default minimal configuration. Some rules might not be checked."));
                    }
                    else
                    {
                        _logger.LogWarning("Fallback ESLint configuration not found at {Path}", fallbackSource);
                    }
                }

                try
                {
                    var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                    
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = isWindows ? "cmd.exe" : "eslint",
                        Arguments = isWindows ? $"/c eslint \"{targetPath}\" --format json" : $"\"{targetPath}\" --format json",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = workingDir
                    };

                    using var process = new Process { StartInfo = startInfo };
                    if (!process.Start()) throw new InvalidOperationException("Failed to start ESLint process.");

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException ex)
                    {
                        _logger.LogWarning(ex, "ESLint timed out for {Path}", targetPath);
                        process.Kill(true);
                        skipped.Add(new SkippedAnalyzer("ESLint", "Analysis timed out after 30 seconds."));
                        return (issues, skipped);
                    }

                    var output = await outputTask;
                    var error = await errorTask;

                    if (!string.IsNullOrWhiteSpace(output) && output.TrimStart().StartsWith('['))
                    {
                        try
                        {
                            var eslintResults = JsonSerializer.Deserialize<List<EslintFileResult>>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (eslintResults != null)
                            {
                                foreach (var fileResult in eslintResults)
                                {
                                    foreach (var msg in fileResult.Messages)
                                    {
                                        issues.Add(new DiagnosticIssue(
                                            msg.RuleId ?? "ESLint",
                                            fileResult.FilePath,
                                            msg.Line,
                                            msg.Column,
                                            msg.Message,
                                            MapSeverity(msg.Severity),
                                            "ESLint"
                                        ));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse ESLint output for {Path}. Output head: {Output}", path, output.Length > 100 ? output.Substring(0, 100) : output);
                            skipped.Add(new SkippedAnalyzer("ESLint", "Failed to parse ESLint output. This might be due to a syntax error or configuration issue."));
                        }
                    }
                    else if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                    {
                        _logger.LogWarning("ESLint finished with code {ExitCode}. Error: {Error}", process.ExitCode, error);
                        
                        if (error.Contains("Parsing error:", StringComparison.OrdinalIgnoreCase) || 
                            error.Contains("SyntaxError:", StringComparison.OrdinalIgnoreCase))
                        {
                            skipped.Add(new SkippedAnalyzer("ESLint", "Analysis skipped due to syntax errors in the file. ESLint cannot parse invalid code."));
                        }
                        else if (error.Contains("Could not find config file", StringComparison.OrdinalIgnoreCase) || 
                                 error.Contains("migrations guide", StringComparison.OrdinalIgnoreCase))
                        {
                             skipped.Add(new SkippedAnalyzer("ESLint", "ESLint configuration error. Ensure a valid eslint.config.js/mjs exists."));
                        }
                        else if (tempConfigPath != null && error.Contains("module not found", StringComparison.OrdinalIgnoreCase))
                        {
                             skipped.Add(new SkippedAnalyzer("ESLint", "Fallback config failed due to missing dependencies (e.g. globals, typescript-eslint). Install them to enable full analysis."));
                        }
                        else
                        {
                             var cleanError = error.Replace("\r", "").Replace("\n", " ").Trim();
                             var shortError = cleanError.Length > 200 ? cleanError.Substring(0, 200) + "..." : cleanError;
                             skipped.Add(new SkippedAnalyzer("ESLint", $"ESLint failed (Exit {process.ExitCode}): {shortError}"));
                        }
                    }
                }
                finally
                {
                    if (tempConfigPath != null && File.Exists(tempConfigPath))
                    {
                        try { File.Delete(tempConfigPath); } catch { /* Ignore cleanup errors */ }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ESLint analysis failed for {Path}", path);
                skipped.Add(new SkippedAnalyzer("ESLint", ex.Message));
            }

            return (issues, skipped);
        }

        private async Task<bool> EnsureEslintDepsInstalled(string workingDir)
        {
            var requiredPackages = new[] { "eslint", "@eslint/js", "typescript-eslint", "globals" };
            
            // Check all at once for speed
            if (await ArePackagesInstalled(workingDir, requiredPackages)) return true;

            if (_autoInstallAttempted) return false;
            _autoInstallAttempted = true;

            _logger.LogInformation("Required ESLint dependencies missing in {Dir}. Attempting local installation (npm install -D {Deps})...", 
                workingDir, string.Join(" ", requiredPackages));
            
            try
            {
                var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                var startInfo = new ProcessStartInfo
                {
                    FileName = isWindows ? "cmd.exe" : "npm",
                    Arguments = isWindows ? $"/c npm install -D {string.Join(" ", requiredPackages)}" : $"install -D {string.Join(" ", requiredPackages)}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDir
                };
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    // Read streams to prevent deadlock
                    _ = process.StandardOutput.ReadToEndAsync();
                    _ = process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    _logger.LogInformation("npm install exited with code {Code}", process.ExitCode);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-installation of ESLint dependencies failed.");
            }

            return false;
        }

        private async Task<bool> ArePackagesInstalled(string workingDir, string[] packages)
        {
            try
            {
                var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                var pkgs = string.Join(" ", packages.Select(p => $"\"{p}\""));

                var startInfo = new ProcessStartInfo
                {
                    FileName = isWindows ? "cmd.exe" : "npm",
                    Arguments = isWindows ? $"/c npm list --depth=0 {pkgs}" : $"list --depth=0 {pkgs}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDir
                };
                using var process = Process.Start(startInfo);
                if (process == null) return false;
                
                // Read streams to prevent deadlock
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check if packages are installed");
                return false;
            }
        }


        private static DiagnosticSeverity MapSeverity(int severity)
        {
            return severity switch
            {
                2 => DiagnosticSeverity.Error,
                1 => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Info
            };
        }

        private sealed record EslintFileResult(
            string FilePath,
            List<EslintMessage> Messages
        );

        private sealed record EslintMessage(
            string? RuleId,
            int Severity,
            string Message,
            int Line,
            int Column
        );
    }
}
