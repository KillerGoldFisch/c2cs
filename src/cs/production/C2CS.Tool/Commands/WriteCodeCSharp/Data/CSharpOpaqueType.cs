// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections.Immutable;

namespace C2CS.Commands.WriteCodeCSharp.Data;

public sealed class CSharpOpaqueType : CSharpNode
{
    public CSharpOpaqueType(
        string name,
        string className,
        string cName)
        : base(name, className, cName, null)
    {
    }

    public override bool Equals(CSharpNode? other)
    {
        if (!base.Equals(other) || other is not CSharpOpaqueType)
        {
            return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        var baseHashCode = base.GetHashCode();
        var hashCode = HashCode.Combine(baseHashCode);
        return hashCode;
    }
}
