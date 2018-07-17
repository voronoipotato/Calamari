﻿using System.IO;
using Calamari.Commands.Support;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Packages;

namespace Calamari.Deployment.Conventions
{
    public class StageScriptPackagesConvention : IInstallConvention
    {
        private readonly string packagePathContainingScript;
        private readonly ICalamariFileSystem fileSystem;

        public StageScriptPackagesConvention(string packagePathContainingScript, ICalamariFileSystem fileSystem)
        {
            this.packagePathContainingScript = packagePathContainingScript;
            this.fileSystem = fileSystem;
        }
        
        public void Install(RunningDeployment deployment)
        {
            // If the script is contained in a package, then extract the containing package in the working directory 
            if (!string.IsNullOrWhiteSpace(packagePathContainingScript))
            {
                ExtractPackage(packagePathContainingScript, deployment.CurrentDirectory, deployment);
                deployment.Variables.Set(SpecialVariables.OriginalPackageDirectoryPath, deployment.CurrentDirectory);
            }
            
            // Stage any referenced packages (i.e. packages that don't contain the script) 
            // The may or may not be extracted.
            StagePackageReferences(deployment);
        }

        void StagePackageReferences(RunningDeployment deployment)
        {
            var variables = deployment.Variables;
            
            var packageReferenceNames = variables.GetIndexes(SpecialVariables.Packages.PackageCollection);

            foreach (var packageReferenceName in packageReferenceNames)
            {
                var sanitizedPackageReferenceName = fileSystem.RemoveInvalidFileNameChars(packageReferenceName);
                
                var packageOriginalPath = variables.Get(SpecialVariables.Packages.OriginalPath(packageReferenceName));
                
                if (string.IsNullOrWhiteSpace(packageOriginalPath))
                {
                    Log.Verbose($"Package '{packageReferenceName}' was not acquired");
                    continue;
                }
                
                packageOriginalPath = Path.GetFullPath(variables.Get(SpecialVariables.Packages.OriginalPath(packageReferenceName)));

                // In the case of container images, the original path is not a file-path.  We won't try and extract or move it.
                if (!fileSystem.FileExists(packageOriginalPath))
                    continue;

                var shouldExtract = variables.GetFlag(SpecialVariables.Packages.Extract(packageReferenceName));

                if (shouldExtract)
                {
                    var extractionPath = Path.Combine(deployment.CurrentDirectory, sanitizedPackageReferenceName);
                    ExtractPackage(packageOriginalPath, extractionPath, deployment);
                    variables.Set(SpecialVariables.Packages.DestinationPath(packageReferenceName), extractionPath);
                }
                else
                {
                    var localPackageFileName = sanitizedPackageReferenceName + Path.GetExtension(packageOriginalPath);
                    var destinationFileName = Path.Combine(deployment.CurrentDirectory, localPackageFileName);
                    Log.Verbose($"Copying package: '{packageOriginalPath}' -> '{destinationFileName}'");
                    fileSystem.CopyFile(packageOriginalPath, destinationFileName);
                    variables.Set(SpecialVariables.Packages.DestinationPath(packageReferenceName), destinationFileName);
                }
            }
        }

        void ExtractPackage(string packageFile, string extractionDirectory, RunningDeployment deployment)
        {
           Log.Verbose($"Extracting package '{packageFile}' to '{extractionDirectory}'"); 
            
            if (!File.Exists(packageFile))
                throw new CommandException("Could not find package file: " + packageFile);
            
            var extractor = new GenericPackageExtractorFactory().createStandardGenericPackageExtractor();
            extractor.GetExtractor(packageFile).Extract(packageFile, extractionDirectory, true);
        }
    }
}