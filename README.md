# VstsGitSolutionAnalyzer

This is a standalone analyzer. 

Overall processing includes the following steps:
1. Connect to VSTS (now Azure DevOps)
2. Get all repositories with develop branch (fallback to master if is not found develop branch)
3. For every repository do:
    1. Download repository locally
    2. Use Nuget to restore packages
    3. Build solution with MSBuild and apply analyzer on build time.
    4. Get received diagnostics from the build
    5. Write received diagnostics into the log (I used Serilog and Sink it into the file)

I used [BlockingAsyncAnalyzer](https://github.com/unsafePtr/BlockingAsyncAnalyzer) to perform analyzing, but you can use whatever analyzer you want.
