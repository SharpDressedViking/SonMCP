using System;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SonMCP.Analysis;
using SonMCP.Reporting;
using SonMCP.Tools;

namespace SonMCP
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // 1. MSBuildLocator must be registered FIRST before any MSBuild/Roslyn types are loaded.
            try
            {
                if (!MSBuildLocator.IsRegistered)
                {
                    MSBuildLocator.RegisterDefaults();
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"FATAL: Failed to register MSBuild SDK: {ex.Message}");
                await Console.Error.WriteLineAsync("Ensure the .NET 8 SDK is installed on this machine.");
                Environment.Exit(1);
            }

            // 2. Build the Host
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    // Log to stderr so stdout is reserved for MCP JSON-RPC
                    logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices((context, services) =>
                {
                    // Core services
                    
                    // Engines
                    services.AddSingleton<IAnalysisEngine, RoslynAnalysisEngine>();
                    services.AddSingleton<IAnalysisEngine, PythonAnalysisEngine>();
                    services.AddSingleton<IAnalysisEngine, JavascriptAnalysisEngine>();
                    services.AddSingleton<AnalysisEngineFactory>();

                    // MCP Server
                    services.AddMcpServer(options =>
                    {
                        options.ServerInfo = new() { Name = "SonMCP", Version = "1.0.0" };
                    })
                    .WithStdioServerTransport()
                    .WithTools<AnalyzeProjectTool>();
                })
                .Build();

            // 3. Run
            await host.RunAsync();
        }
    }
}
