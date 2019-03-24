using VstsGitSolutionAnalyzer.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using RunProcessAsTask;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace VstsGitSolutionAnalyzer
{
    public class AnalyzerProcessor
    {
        private Logger ResultsLogger { get; } = new LoggerConfiguration()
            .WriteTo.File(@"..\..\..\Results.txt")
            .CreateLogger();

        private Logger TestsLogger { get; } = new LoggerConfiguration()
            .WriteTo.File(@"..\..\..\Tests.txt")
            .CreateLogger();

        private Logger ErrorLogger { get; } = new LoggerConfiguration()
            .WriteTo.File(@"..\..\..\Errors.txt")
            .CreateLogger();

        private Logger SkippedLogger { get; } = new LoggerConfiguration()
            .WriteTo.File(@"..\..\..\Skipped.txt")
            .CreateLogger();

        private Logger TraversedLogger { get; } = new LoggerConfiguration()
            .WriteTo.File(@"..\..\..\Traversed.txt")
            .CreateLogger();

        private readonly GitVersionDescriptor masterBracnhDescriptior = new GitVersionDescriptor()
        {
            VersionType = GitVersionType.Branch,
            Version = "master",
            VersionOptions = GitVersionOptions.None
        };

        private readonly GitVersionDescriptor developBranchDescriptior = new GitVersionDescriptor()
        {
            VersionType = GitVersionType.Branch,
            Version = "develop",
            VersionOptions = GitVersionOptions.None
        };

        private VssConnection Connection { get; }

        public AnalyzerProcessor(VssConnection connection)
        {
            Connection = connection;
        }

        private IEnumerable<GitItem> GetBlobGitItems(IEnumerable<GitItem> items)
        {
            return items.Where(i =>
                !i.IsFolder &&
                i.GitObjectType == GitObjectType.Blob &&
                !i.Path.StartsWith("/packages"));
        }

        public async Task GetDiagnosticsAsync(DiagnosticAnalyzer analyzer)
        {
            GitHttpClient gitHttpClient = Connection.GetClient<GitHttpClient>();
            List<GitRepository> repositories = await gitHttpClient.GetRepositoriesAsync(true);

            var tasks = repositories.Select(r => Process(r, analyzer)).ToList();

            await Task.WhenAll(tasks);
        }

        private async Task Process(GitRepository repo, DiagnosticAnalyzer analyzer)
        {
            try
            {
                GitHttpClient client = Connection.GetClient<GitHttpClient>();

                string branchName;
                IEnumerable<GitItem> filteredItems = new List<GitItem>();
                try
                {
                    branchName = "develop";
                    List<GitItem> items = await client.GetItemsAsync(repo.Id, scopePath: "/", recursionLevel: VersionControlRecursionType.Full, versionDescriptor: developBranchDescriptior, latestProcessedChange: true);
                    filteredItems = GetBlobGitItems(items);
                    if (!filteredItems.Any(i => i.Path.Contains(".sln")))
                    {
                        SkippedLogger.Information("{RepositoryName}: No silution file in {BranchName} branch", repo.Name, branchName);
                        return;
                    }
                }
                catch (VssServiceException) // fallback to master branch
                {
                    branchName = "master";
                    List<GitItem> items = await client.GetItemsAsync(repo.Id, scopePath: "/", recursionLevel: VersionControlRecursionType.Full, versionDescriptor: masterBracnhDescriptior, latestProcessedChange: true);
                    filteredItems = GetBlobGitItems(items);
                    if (!filteredItems.Any(i => i.Path.Contains(".sln")))
                    {
                        SkippedLogger.Information("{RepositoryName}: No silution file in {BranchName} branch", repo.Name, branchName);
                        return;
                    }
                }

                string extractPath = $@"..\..\..\Repo\{repo.Name}";
                Directory.CreateDirectory(extractPath);
                string solutionPath = Directory.GetFiles(extractPath, "*.sln", SearchOption.AllDirectories).FirstOrDefault();

                List<Diagnostic> diagnostics = new List<Diagnostic>();
                foreach (var item in filteredItems)
                {
                    GitVersionDescriptor commitDescriptior = new GitVersionDescriptor()
                    {
                        VersionType = GitVersionType.Commit,
                        Version = item.CommitId,
                        VersionOptions = GitVersionOptions.None
                    };

                    string[] pathSegments = item.Path.Split('/');
                    string folderPath = String.Join(@"\", pathSegments.Take(pathSegments.Length - 1));
                    string itemPath = String.Concat(extractPath, folderPath, @"\", pathSegments.Last());

                    using (Stream itemZip = await client.GetItemZipAsync(repo.Id, item.Path, versionDescriptor: commitDescriptior))
                    using (ZipArchive zipStream = new ZipArchive(itemZip))
                    {
                        if (Path.GetDirectoryName(item.Path) != @"\")
                        {
                            Directory.CreateDirectory(extractPath + folderPath);
                        }

                        foreach (var entry in zipStream.Entries)
                        {
                            entry.ExtractToFile(itemPath, true);
                        }
                    }
                }

                ProcessResults processResults = await NuGetRestoreHelper.RestoreAsync(Path.GetDirectoryName(solutionPath));
                if (processResults == null)
                {
                    ErrorLogger.Information("{RepositoryName}: Can't restore nuget", repo.Name);
                    return;
                }

                if (!String.IsNullOrEmpty(String.Join(Environment.NewLine, processResults.StandardError)))
                {
                    Exception ex = new Exception($"Error: {String.Join(Environment.NewLine, processResults.StandardError)}, Output: { processResults.StandardOutput}");
                    string template = "{RepositoryName}: {@Exception}";
                    ErrorLogger.Information(
                        template,
                        repo.Name,
                        ex
                    );

                    return;
                }

                try
                {
                    BuildLocatorHelper locator = new BuildLocatorHelper();
                    Solution solution = await locator.OpenSolutionAsync(solutionPath);
                    ProjectDependencyGraph projectGraph = solution.GetProjectDependencyGraph();

                    foreach (ProjectId projectId in projectGraph.GetTopologicallySortedProjects())
                    {
                        Project project = solution.GetProject(projectId);
                        try
                        {
                            Compilation compilation = await project.GetCompilationAsync();
                            var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
                            var diags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
                            foreach (var diag in diags)
                            {
                                diagnostics.Add(diag);
                            }
                        }
                        catch (Exception ex)
                        {
                            ErrorLogger.Information("Can't compile project. Repository: {RepositoryName}. Project: {ProjectName}. Exception: {@Exception}", repo.Name, project.Name, ex);
                            // ignore compilations errors
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLogger.Information("{RepositoryName}: can't build solution: {@Exception}", repo.Name, ex);
                }

                foreach (var diagnostic in diagnostics)
                {
                    string filePath = diagnostic.Location.SourceTree.FilePath;
                    var linePosition = diagnostic.Location.GetLineSpan().StartLinePosition;
                    string template = "Branch: {BranchName}. Repository: {RepositoryName}. File: {FilePath}. {AnalyzerName}:{AnalyzerMessage}, {LinePosition}:{CharachterPosition}";


                    if (filePath.Contains(".Tests") || filePath.Contains(".Test"))
                    {
                        TestsLogger.Information(
                            template,
                            branchName,
                            repo.Name,
                            filePath,
                            analyzer.GetType().Name,
                            diagnostic.GetMessage(),
                            linePosition.Line + 1,
                            linePosition.Character + 1
                        );
                    }
                    else
                    {
                        ResultsLogger.Information(
                            template,
                            branchName,
                            repo.Name,
                            filePath,
                            analyzer.GetType().Name,
                            diagnostic.GetMessage(),
                            linePosition.Line + 1,
                            linePosition.Character + 1
                        );
                    }
                }

                TraversedLogger.Information("{RepositoryName}", repo.Name);
            }
            catch (Exception ex)
            {
                ErrorLogger.Information("{RepositoryName}: {@Exception}", repo.Name, ex);
            }
        }
    }
}
