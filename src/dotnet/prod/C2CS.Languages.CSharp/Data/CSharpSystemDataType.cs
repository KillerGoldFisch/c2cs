// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory (https://github.com/lithiumtoast/c2cs) for full license information.

namespace C2CS.CSharp
{
    public record CSharpSystemDataType : CSharpCommon
    {
        public readonly CSharpType UnderlyingType;

        public CSharpSystemDataType(
            string name,
            string originalCodeLocationComment,
            CSharpType underlyingType)
            : base(name, originalCodeLocationComment)
        {
            UnderlyingType = underlyingType;
        }

        // Required for debugger string with records
        // ReSharper disable once RedundantOverriddenMember
        public override string ToString()
        {
            return base.ToString();
        }
    }
}
