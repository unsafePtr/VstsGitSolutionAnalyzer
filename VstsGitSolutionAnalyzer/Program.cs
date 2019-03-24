using VstsGitSolutionAnalyzer;
using AsyncPackage;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Threading.Tasks;

namespace VstsGitSolutionAnalyzer
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Processing");

            VssBasicCredential credintials = new VssBasicCredential(String.Empty, "Azure DevOps Key") { };
            VssConnection connection = new VssConnection(new Uri("VisualStudioOnline server url"), credintials);
            AnalyzerProcessor processor = new AnalyzerProcessor(connection);
            DiagnosticAnalyzer analyzer = new BlockingAsyncAnalyzer();
            await processor.GetDiagnosticsAsync(analyzer);

            Console.WriteLine("Ended");

            Console.ReadKey(true);
        }
    }
}
