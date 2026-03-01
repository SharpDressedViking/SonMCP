using System.Collections.Generic;
using System.Threading.Tasks;
using SonMCP.Reporting;

namespace SonMCP.Analysis
{
    public interface IAnalysisEngine
    {
        /// <summary>
        /// Returns true if this engine can handle the given file extension.
        /// </summary>
        bool CanAnalyze(string extension);

        /// <summary>
        /// Analyzes the project or file at the given path.
        /// </summary>
        Task<(IEnumerable<DiagnosticIssue> Issues, IEnumerable<SkippedAnalyzer> Skipped)> AnalyzeAsync(string path);
    }
}
