// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.Linq;
using JetBrains.Annotations;

namespace C2CS.Features.BuildCLibrary;

[UsedImplicitly]
public sealed class BuildCLibraryCommand : Command
{
    private readonly BuildCLibraryTool _tool;

    public BuildCLibraryCommand(BuildCLibraryTool tool)
        : base(
            "library",
            "Build a C library for the purposes of FFI (foreign function interface) with other languages such as C#.")
    {
        _tool = tool;

        var configurationFilePathOption = new Option<string>(
            "--config", "The file path configure C# bindgen.");
        configurationFilePathOption.SetDefaultValue("config.json");
        AddOption(configurationFilePathOption);

        var additionalCMakeArgumentsOption = new Option<string?[]>(
            "--cmake-arguments", "CMake arguments provided on command line instead of in the configuration file.")
        {
            AllowMultipleArgumentsPerToken = true
        };
        AddOption(additionalCMakeArgumentsOption);

        this.SetHandler(Main, configurationFilePathOption, additionalCMakeArgumentsOption);
    }

    private void Main(string configurationFilePath, string?[] additionalCMakeArguments)
    {
        // Hack: Property injection for command line CMake arguments
        _tool.AdditionalCMakeArguments = additionalCMakeArguments
            .Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToImmutableArray();

        var output = _tool.Run(configurationFilePath);
        if (!output.IsSuccess)
        {
            Environment.Exit(1);
        }
    }
}
