using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SonMCP.Analysis
{
    public class AnalysisEngineFactory
    {
        private readonly IEnumerable<IAnalysisEngine> _engines;

        public AnalysisEngineFactory(IEnumerable<IAnalysisEngine> engines)
        {
            _engines = engines;
        }

        public IAnalysisEngine GetEngine(string path, string? languageHint = null)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            
            // 1. Explicit language hint (highest priority)
            if (!string.IsNullOrWhiteSpace(languageHint))
            {
                var engine = languageHint.ToLowerInvariant() switch
                {
                    "python" => _engines.FirstOrDefault(e => e.CanAnalyze(".py")),
                    "javascript" or "js" => _engines.FirstOrDefault(e => e.CanAnalyze(".js")),
                    "typescript" or "ts" => _engines.FirstOrDefault(e => e.CanAnalyze(".ts")),
                    _ => null
                };
                if (engine != null) return engine;
            }

            // 2. Extension based
            if (!string.IsNullOrEmpty(extension))
            {
                var engine = _engines.FirstOrDefault(e => e.CanAnalyze(extension));
                if (engine != null) return engine;
            }

            // 3. Fallback/Guessing for directories
            if (Directory.Exists(path))
            {
                 // We could look for .sln, package.json, etc. 
                 // For now, if no hint and it's a directory, we ask for hint or throw better error
                 throw new NotSupportedException($"Direction analysis for '{path}' requires a language hint if no C#/.NET project file is found. Try providing 'python' or 'javascript'.");
            }

            throw new NotSupportedException($"No analysis engine found for target '{path}'. Supported extensions: .csproj, .sln, .vbproj, .py, .js, .ts, .jsx, .tsx");
        }
    }
}
