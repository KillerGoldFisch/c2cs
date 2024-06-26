// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace C2CS.Commands.WriteCodeCSharp.Input.Unsanitized;

/// <summary>
///     A pair of source and target names for renaming.
/// </summary>
// NOTE: This class is considered un-sanitized input; all strings and other types could be null.
[PublicAPI]
public sealed class WriteCSharpCodeInputMappedName
{
    /// <summary>
    ///     Gets or sets the name to rename.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the renamed name.
    /// </summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; } = string.Empty;
}
