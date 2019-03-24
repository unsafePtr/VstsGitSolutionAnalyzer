using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace VstsGitSolutionAnalyzer.Helpers
{
    class BuildLocatorHelper
    {
        public MSBuildWorkspace Workspace { get; private set; }

        private static VisualStudioInstance instance;
        private static VisualStudioInstance VisualStudioInstance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }
                else
                {
                    var instances = MSBuildLocator.QueryVisualStudioInstances();
                    instance = instances.FirstOrDefault();
                    if (instance != null)
                    {
                        // register the instance whose version matches with the hinted one
                        MSBuildLocator.RegisterInstance(instance);
                    }

                    if (instance == null)
                    {
                        // register the default instead
                        instance = MSBuildLocator.RegisterDefaults();
                    }

                    return instance;
                }
            }
        }


        public BuildLocatorHelper()
        {
            var inst = VisualStudioInstance;
            Debug.WriteLine($"BuildHelper: registered instance: {GetVisualStudioString(inst)}");

            Workspace = MSBuildWorkspace.Create();
        }

        public async Task<Solution> OpenSolutionAsync(string solutionFullPath)
        {
            var solution = await Workspace.OpenSolutionAsync(solutionFullPath);
            return solution;
        }


        private string GetVisualStudioString(VisualStudioInstance visualStudio)
        {
            if (visualStudio == null) return "(not found)";

            return $"{visualStudio.Name} {visualStudio.Version}";
        }
    }
}
