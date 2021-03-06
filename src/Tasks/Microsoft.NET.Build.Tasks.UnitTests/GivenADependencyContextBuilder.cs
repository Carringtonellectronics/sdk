﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentAssertions.Json;
using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using Xunit;

namespace Microsoft.NET.Build.Tasks.UnitTests
{
    public class GivenADependencyContextBuilder
    {
        /// <summary>
        /// Tests that DependencyContextBuilder generates DependencyContexts correctly.
        /// </summary>
        [Theory]
        [MemberData(nameof(ProjectData))]
        public void ItBuildsDependencyContextsFromProjectLockFiles(
            string mainProjectName,
            string mainProjectVersion,
            CompilationOptions compilationOptions,
            string baselineFileName,
            string runtime,
            ITaskItem[] assemblySatelliteAssemblies,
            ITaskItem[] referencePaths,
            ITaskItem[] referenceSatellitePaths)
        {
            LockFile lockFile = TestLockFiles.GetLockFile(mainProjectName);

            SingleProjectInfo mainProject = SingleProjectInfo.Create(
                "/usr/Path",
                mainProjectName,
                ".dll",
                mainProjectVersion,
                assemblySatelliteAssemblies ?? new ITaskItem[] { });

            IEnumerable<ReferenceInfo> directReferences =
                ReferenceInfo.CreateDirectReferenceInfos(
                    referencePaths ?? new ITaskItem[] { }, 
                    referenceSatellitePaths ?? new ITaskItem[] { });

            ProjectContext projectContext = lockFile.CreateProjectContext(
                FrameworkConstants.CommonFrameworks.NetCoreApp10,
                runtime,
                Constants.DefaultPlatformLibrary,
                isSelfContained: !string.IsNullOrEmpty(runtime));

            DependencyContext dependencyContext = new DependencyContextBuilder(mainProject, projectContext)
                .WithDirectReferences(directReferences)
                .WithCompilationOptions(compilationOptions)
                .Build();

            JObject result = Save(dependencyContext);
            JObject baseline = ReadJson($"{baselineFileName}.deps.json");

            try
            {
                baseline
                    .Should()
                    .BeEquivalentTo(result);
            }
            catch
            {
                // write the result file out on failure for easy comparison

                using (JsonTextWriter writer = new JsonTextWriter(File.CreateText($"result-{baselineFileName}.deps.json")))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Formatting = Formatting.Indented;
                    serializer.Serialize(writer, result);
                }

                throw;
            }
        }

        public static IEnumerable<object[]> ProjectData
        {
            get
            {
                CompilationOptions compilationOptions = new CompilationOptions(
                    defines: new[] { "DEBUG", "TRACE" },
                    languageVersion: "6",
                    platform: "x64",
                    allowUnsafe: true,
                    warningsAsErrors: false,
                    optimize: null,
                    keyFile: "../keyfile.snk",
                    delaySign: null,
                    publicSign: null,
                    debugType: "portable",
                    emitEntryPoint: true,
                    generateXmlDocumentation: true);

                ITaskItem[] dotnetNewSatelliteAssemblies = new ITaskItem[]
                {
                    new MockTaskItem(
                        @"de\dotnet.new.resources.dll",
                        new Dictionary<string, string>
                        {
                            { "Culture", "de" },
                            { "TargetPath", @"de\dotnet.new.resources.dll" },
                        }),
                    new MockTaskItem(
                        @"fr\dotnet.new.resources.dll",
                        new Dictionary<string, string>
                        {
                            { "Culture", "fr" },
                            { "TargetPath", @"fr\dotnet.new.resources.dll" },
                        }),
                };

                ITaskItem[] referencePaths = new ITaskItem[]
                {
                    new MockTaskItem(
                        "/usr/Path/RandomLooseLibrary.dll",
                        new Dictionary<string, string>
                        {
                            { "CopyLocal", "true" },
                            { "FusionName", "RandomLooseLibrary, Version=1.2.0.4, Culture=neutral, PublicKeyToken=null" },
                            { "ReferenceSourceTarget", "ResolveAssemblyReference" },
                            { "Version", "" },
                        }),
                };

                ITaskItem[] referenceSatellitePaths = new ITaskItem[]
                {
                    new MockTaskItem(
                        @"/usr/Path/fr/RandomLooseLibrary.resources.dll",
                        new Dictionary<string, string>
                        {
                            { "CopyLocal", "true" },
                            { "DestinationSubDirectory", "fr/" },
                            { "OriginalItemSpec", "/usr/Path/RandomLooseLibrary.dll" },
                            { "ResolvedFrom", "{RawFileName}" },
                            { "Version", "" },
                        }),
                };

                return new[]
                {
                    new object[] { "dotnet.new", "1.0.0", null, "dotnet.new", null, null, null, null},
                    new object[] { "dotnet.new", "1.0.0", null, "dotnet.new.resources", null, dotnetNewSatelliteAssemblies, null, null },
                    new object[] { "simple.dependencies", "1.0.0", null, "simple.dependencies", null, null, null, null },
                    new object[] { "simple.dependencies", "1.0.0", compilationOptions, "simple.dependencies.compilerOptions", null, null, null, null},
                    new object[] { "simple.dependencies", "1.0.0", compilationOptions, "simple.dependencies.directReference", null, null, referencePaths, referenceSatellitePaths},
                    new object[] { "all.asset.types", "1.0.0", null, "all.asset.types.portable", null, null, null, null },
                    new object[] { "all.asset.types", "1.0.0", null, "all.asset.types.osx", "osx.10.11-x64", null, null, null },
                };
            }
        }

        private static JObject ReadJson(string path)
        {
            using (JsonTextReader jsonReader = new JsonTextReader(File.OpenText(path)))
            {
                JsonSerializer serializer = new JsonSerializer();
                return serializer.Deserialize<JObject>(jsonReader);
            }
        }

        private JObject Save(DependencyContext dependencyContext)
        {
            using (var memoryStream = new MemoryStream())
            {
                new DependencyContextWriter().Write(dependencyContext, memoryStream);
                using (var readStream = new MemoryStream(memoryStream.ToArray()))
                {
                    using (var textReader = new StreamReader(readStream))
                    {
                        using (var reader = new JsonTextReader(textReader))
                        {
                            return JObject.Load(reader);
                        }
                    }
                }
            }
        }
    }
}
