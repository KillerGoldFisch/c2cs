// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace C2CS.UseCases.AbstractSyntaxTreeC;

// NOTE: Properties are required for System.Text.Json serialization
[PublicAPI]
public record CFunctionPointerParameter : CNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    public override string ToString()
    {
        return $"FunctionPointerParameter '{Name}': {Type} @ {Location.ToString()}";
    }
}
