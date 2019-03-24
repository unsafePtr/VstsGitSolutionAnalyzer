using RunProcessAsTask;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VstsGitSolutionAnalyzer.Helpers
{
    public static class NuGetRestoreHelper
    {
        /// <summary>
        /// Add folder where is located nuget to PATH variable
        /// </summary>
        private static string NuGetPath = @"nuget";

        public static async Task<ProcessResults> RestoreAsync(string inputPath)
        {
            var args = new string[] {
                "restore",
                inputPath,
                "-Verbosity",
                "detailed"
            };

            ProcessStartInfo processStartInfo = new ProcessStartInfo()
            {
                FileName = NuGetPath,
                Arguments = String.Join(" ", args)
            };

            int timeout = 240000; // 3 minutes
            CancellationTokenSource source = new CancellationTokenSource(timeout);
            try
            {
                return await ProcessEx.RunAsync(processStartInfo, source.Token);
            }
            catch
            {
                return null;
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}
