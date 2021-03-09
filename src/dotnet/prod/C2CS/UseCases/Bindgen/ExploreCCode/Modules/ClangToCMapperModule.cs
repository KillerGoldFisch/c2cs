// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory (https://github.com/lithiumtoast/c2cs) for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using ClangSharp.Interop;

namespace C2CS.Bindgen.ExploreCCode
{
    public class ClangToCMapperModule
    {
        private readonly ImmutableDictionary<CXCursor, string> _namesByCursor;
        private readonly ImmutableDictionary<CXCursor, ImmutableArray<CXCursor>> _functionParametersByFunction;
        private readonly ImmutableDictionary<CXCursor, ImmutableArray<CXCursor>> _recordFieldsByRecord;
        private readonly ImmutableDictionary<CXCursor, ImmutableArray<CXCursor>> _enumValuesByEnum;
        private readonly Dictionary<CXType, CTypeLayout> _layoutsByClangType = new();

        public ClangToCMapperModule(
            ImmutableDictionary<CXCursor, string> namesByCursor,
            ImmutableDictionary<CXCursor, ImmutableArray<CXCursor>> functionParametersByFunction,
            ImmutableDictionary<CXCursor, ImmutableArray<CXCursor>> recordFieldsByRecord,
            ImmutableDictionary<CXCursor, ImmutableArray<CXCursor>> enumValuesByEnum)
        {
            _namesByCursor = namesByCursor;
            _functionParametersByFunction = functionParametersByFunction;
            _recordFieldsByRecord = recordFieldsByRecord;
            _enumValuesByEnum = enumValuesByEnum;
        }

        public ImmutableArray<CFunction> MapFunctions(ImmutableArray<CXCursor> clangFunctions)
        {
            var builder = ImmutableArray.CreateBuilder<CFunction>(clangFunctions.Length);

            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var clangFunction in clangFunctions)
            {
                var cFunction = MapFunction(clangFunction);
                builder.Add(cFunction);
            }

            var result = builder.ToImmutable();

            return result;
        }

        public ImmutableArray<CStruct> MapStructs(ImmutableArray<CXCursor> clangRecords)
        {
            var builder = ImmutableArray.CreateBuilder<CStruct>(clangRecords.Length);

            foreach (var clangRecord in clangRecords)
            {
                var cStruct = MapStruct(clangRecord);
                builder.Add(cStruct);
            }

            var result = builder.ToImmutable();

            return result;
        }

        public ImmutableArray<CFunctionPointer> MapFunctionPointers(ImmutableArray<CXCursor> clangFunctionProtos)
        {
            var builder = ImmutableArray.CreateBuilder<CFunctionPointer>(clangFunctionProtos.Length);

            foreach (var clangFunctionProto in clangFunctionProtos)
            {
                var cFunctionPointer = MapFunctionPointer(clangFunctionProto);
                builder.Add(cFunctionPointer);
            }

            var result = builder.ToImmutable();

            return result;
        }

        public ImmutableArray<COpaqueType> MapOpaqueTypes(ImmutableArray<CXCursor> clangOpaqueTypes)
        {
            var builder = ImmutableArray.CreateBuilder<COpaqueType>(clangOpaqueTypes.Length);

            foreach (var clangOpaqueType in clangOpaqueTypes)
            {
                var cOpaqueType = MapOpaqueType(clangOpaqueType);
                builder.Add(cOpaqueType);
            }

            var result = builder.ToImmutable();

            return result;
        }

        public ImmutableArray<CEnum> MapEnums(ImmutableArray<CXCursor> clangEnums)
        {
            var builder = ImmutableArray.CreateBuilder<CEnum>(clangEnums.Length);

            foreach (var clangEnum in clangEnums)
            {
                var cEnum = MapEnum(clangEnum);
                builder.Add(cEnum);
            }

            var result = builder.ToImmutable();

            return result;
        }

        private CEnum MapEnum(CXCursor clangEnum)
        {
            var name = _namesByCursor[clangEnum];
            var location = MapLocation(clangEnum);
            var type = MapType(clangEnum.EnumDecl_IntegerType);
            var enumValues = MapEnumValues(clangEnum);

            var result = new CEnum(
                name,
                location,
                type,
                enumValues);

            return result;
        }

        private ImmutableArray<CEnumValue> MapEnumValues(CXCursor clangEnum)
        {
            var clangEnumValues = _enumValuesByEnum[clangEnum];
            var builder = ImmutableArray.CreateBuilder<CEnumValue>(clangEnumValues.Length);

            foreach (var clangEnumValue in clangEnumValues)
            {
                var cEnumValue = MapEnumValue(clangEnumValue);
                builder.Add(cEnumValue);
            }

            var result = builder.ToImmutable();
            return result;
        }

        private CEnumValue MapEnumValue(CXCursor clangEnumValue)
        {
            var name = clangEnumValue.Spelling.CString;
            var value = clangEnumValue.EnumConstantDeclValue;

            var result = new CEnumValue(
                name,
                value);

            return result;
        }

        private COpaqueType MapOpaqueType(CXCursor clangOpaqueType)
        {
            var name = MapTypeName(clangOpaqueType.Type);
            var location = MapLocation(clangOpaqueType);
            var type = MapType(clangOpaqueType.Type);

            var result = new COpaqueType(
                name,
                location,
                type);

            return result;
        }

        private CFunctionPointer MapFunctionPointer(CXCursor clangFunctionProto)
        {
            var name = _namesByCursor[clangFunctionProto];
            var location = MapLocation(clangFunctionProto);
            var type = MapType(clangFunctionProto.Type);

            var result = new CFunctionPointer(
                name,
                location,
                type);

            return result;
        }

        private CFunction MapFunction(CXCursor clangFunction)
        {
            var name = MapName(clangFunction);
            var location = MapLocation(clangFunction);
            var returnType = MapType(clangFunction.ResultType);
            var callingConvention = MapFunctionCallingConvention(clangFunction);
            var parameters = MapFunctionParameters(clangFunction);

            var result = new CFunction(
                name,
                location,
                returnType,
                callingConvention,
                parameters);

            return result;
        }

        private CFunctionCallingConvention MapFunctionCallingConvention(CXCursor clangFunction)
        {
            var clangCallingConvention = clangFunction.Type.FunctionTypeCallingConv;

            var callingConvention = clangCallingConvention switch
            {
                CXCallingConv.CXCallingConv_C => CFunctionCallingConvention.C,
                _ => throw new NotImplementedException()
            };

            return callingConvention;
        }

        private ImmutableArray<CFunctionParameter> MapFunctionParameters(CXCursor clangFunction)
        {
            var clangFunctionParameters = _functionParametersByFunction[clangFunction];

            var builder = ImmutableArray.CreateBuilder<CFunctionParameter>(clangFunctionParameters.Length);
            for (var i = 0; i < clangFunctionParameters.Length; i++)
            {
                var clangFunctionParameter = clangFunctionParameters[i];
                var cFunctionParameter = MapFunctionParameter(clangFunctionParameter, i);
                builder.Add(cFunctionParameter);
            }

            var result = builder.ToImmutable();

            return result;
        }

        private CFunctionParameter MapFunctionParameter(CXCursor clangFunctionParameter, int index)
        {
            var functionParameterName = MapName(clangFunctionParameter);
            if (string.IsNullOrEmpty(functionParameterName))
            {
                if (index == 0)
                {
                    functionParameterName = "param";
                }
                else
                {
                    functionParameterName = "param" + (index + 1);
                }
            }

            var functionParameterType = MapType(clangFunctionParameter.Type);
            var functionIsReadOnly = clangFunctionParameter.Type.IsConstQualified;

            var result = new CFunctionParameter(
                functionParameterName,
                functionParameterType,
                functionIsReadOnly);

            return result;
        }

        private CStruct MapStruct(CXCursor clangRecord)
        {
            var name = MapName(clangRecord);
            var info = MapLocation(clangRecord);
            var type = MapType(clangRecord.Type);
            var fields = MapStructFields(clangRecord);

            var result = new CStruct(
                name,
                info,
                type,
                fields);

            return result;
        }

        private ImmutableArray<CStructField> MapStructFields(CXCursor clangRecord)
        {
            var clangRecordFields = _recordFieldsByRecord[clangRecord];

            var builder = ImmutableArray.CreateBuilder<CStructField>();
            foreach (var clangRecordField in clangRecordFields)
            {
                var cField = MapStructField(clangRecordField);
                builder.Add(cField);
            }

            var result = builder.ToImmutable();

            return result;
        }

        private CStructField MapStructField(CXCursor clangRecordField)
        {
            var name = MapName(clangRecordField);
            var type = MapType(clangRecordField.Type);

            var result = new CStructField(
                name,
                type);

            return result;
        }

        private CType MapType(CXType clangType)
        {
            var name = MapTypeName(clangType);
            var originalName = clangType.Spelling.CString;
            var arraySize = MapTypeArraySize(clangType);
            var layout = MapLayout(clangType);

            var result = new CType(
                name,
                originalName,
                arraySize,
                layout);

            return result;
        }

        private string MapTypeName(CXType clangType)
        {
            var typeName = clangType.TypeClass switch
            {
                CX_TypeClass.CX_TypeClass_Builtin => MapTypeNameBuiltIn(clangType),
                CX_TypeClass.CX_TypeClass_Pointer => MapTypeNamePointer(clangType),
                CX_TypeClass.CX_TypeClass_Typedef => MapTypeNameTypedef(clangType),
                CX_TypeClass.CX_TypeClass_Elaborated => MapTypeNameElaborated(clangType),
                CX_TypeClass.CX_TypeClass_Record => MapTypeNameRecord(clangType),
                CX_TypeClass.CX_TypeClass_Enum => MapTypeNameEnum(clangType),
                CX_TypeClass.CX_TypeClass_ConstantArray => MapTypeNameConstArray(clangType),
                CX_TypeClass.CX_TypeClass_FunctionProto => MapTypeNameFunctionProto(clangType),
                // CX_TypeClass.CX_TypeClass_Record => ParseTypeName(baseClangTypeSpelling.Replace("struct ", string.Empty)
                //     .Trim()),
                // CX_TypeClass.CX_TypeClass_Elaborated => GetCSharpType(clangType.NamedType, baseClangType),
                _ => throw new NotImplementedException()
            };

            if (clangType.IsConstQualified)
            {
                typeName = typeName.Replace("const ", string.Empty).Trim();
            }

            return typeName;
        }

        private string MapTypeNameFunctionProto(CXType clangType)
        {
            return "void*";
        }

        private string MapTypeNameRecord(CXType clangType)
        {
            var clangRecord = clangType.Declaration;

            var result = clangRecord.IsAnonymous
                ? MapTypeNameRecordAnonymous(clangRecord)
                : MapTypeNameRecordNonAnonymous(clangRecord);

            return result;
        }

        private string MapTypeNameRecordAnonymous(CXCursor clangRecord)
        {
            var parent = clangRecord.SemanticParent;
            string parentFieldName = MapName(parent);

            var result = clangRecord.kind == CXCursorKind.CXCursor_UnionDecl
                ? $"Anonymous_Union_{parentFieldName}"
                : $"Anonymous_Struct_{parentFieldName}";

            return result;
        }

        private static string MapTypeNameRecordNonAnonymous(CXCursor clangRecord)
        {
            var result = clangRecord.Type.Spelling.CString;
            if (result.Contains("struct "))
            {
                result = result.Replace("struct ", string.Empty);
            }

            return result;
        }

        private string MapTypeNameEnum(CXType clangType)
        {
            var result = clangType.Spelling.CString;
            if (result.Contains("enum "))
            {
                result = result.Replace("enum ", string.Empty);
            }

            return result;
        }

        private string MapTypeNameConstArray(CXType clangType)
        {
            var elementType = clangType.ArrayElementType;
            var result = MapTypeName(elementType);

            return result;
        }

        private string MapTypeNameBuiltIn(CXType clangType)
        {
            var result = clangType.kind switch
            {
                CXTypeKind.CXType_Void => "void",
                CXTypeKind.CXType_Bool => "CBool",
                CXTypeKind.CXType_Char_S => "sbyte",
                CXTypeKind.CXType_Char_U => "byte",
                CXTypeKind.CXType_UChar => "byte",
                CXTypeKind.CXType_UShort => "ushort",
                CXTypeKind.CXType_UInt => "uint",
                CXTypeKind.CXType_ULong => clangType.SizeOf == 8 ? "ulong" : "uint",
                CXTypeKind.CXType_ULongLong => "ulong",
                CXTypeKind.CXType_Short => "short",
                CXTypeKind.CXType_Int => "int",
                CXTypeKind.CXType_Long => clangType.SizeOf == 8 ? "long" : "int",
                CXTypeKind.CXType_LongLong => "long",
                CXTypeKind.CXType_Float => "float",
                CXTypeKind.CXType_Double => "double",
                CXTypeKind.CXType_Record => clangType.Spelling.CString.Replace("struct ", string.Empty).Trim(),
                CXTypeKind.CXType_Enum => clangType.Spelling.CString.Replace("enum ", string.Empty).Trim(),
                _ => throw new NotImplementedException()
            };

            return result;
        }

        private string MapTypeNamePointer(CXType clangType)
        {
            var clangPointeeType = clangType.PointeeType;
            if (clangPointeeType.TypeClass == CX_TypeClass.CX_TypeClass_FunctionProto)
            {
                return "void*";
            }

            var pointeeTypeName = MapTypeName(clangPointeeType);
            var result = pointeeTypeName + "*";

            return result;
        }

        private string MapTypeNameTypedef(CXType clangType)
        {
            var isInSystem = clangType.Declaration.IsInSystem();

            var result = isInSystem
                ? MapTypeNameTypedefSystem(clangType)
                : MapTypeNameTypedefNonSystem(clangType);

            return result;
        }

        private string MapTypeNameTypedefSystem(CXType clangType)
        {
            var clangUnderlyingType = clangType.Declaration.TypedefDeclUnderlyingType;

            string result = clangUnderlyingType.TypeClass == CX_TypeClass.CX_TypeClass_Builtin
                ? MapTypeNameBuiltIn(clangUnderlyingType.CanonicalType)
                : MapTypeName(clangType.CanonicalType);

            return result;
        }

        private static string MapTypeNameTypedefNonSystem(CXType clangType)
        {
            var result = clangType.TypedefName.CString;

            return result;
        }

        private string MapTypeNameElaborated(CXType clangType)
        {
            var clangNamedType = clangType.NamedType;
            var result = MapTypeName(clangNamedType);

            return result;
        }

        private string MapCursorName(CXCursor clangCursor)
        {
            if (clangCursor.IsInSystem())
            {
                var systemUnderlyingType = clangCursor.Type.CanonicalType;
                if (systemUnderlyingType.TypeClass == CX_TypeClass.CX_TypeClass_Builtin)
                {
                    return MapTypeNameBuiltIn(systemUnderlyingType);
                }

                throw new NotImplementedException();
            }

            if (clangCursor.IsAnonymous)
            {
                var fieldName = string.Empty;
                var parent = clangCursor.SemanticParent;
                parent.VisitChildren((child, __) =>
                {
                    if (child.kind == CXCursorKind.CXCursor_FieldDecl && child.Type.Declaration == clangCursor)
                    {
                        fieldName = child.Spelling.CString;
                    }
                });

                return clangCursor.kind == CXCursorKind.CXCursor_UnionDecl
                    ? $"Anonymous_Union_{fieldName}"
                    : $"Anonymous_Struct_{fieldName}";
            }

            var name = clangCursor.Spelling.CString;
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            var clangType = clangCursor.Type;
            if (clangType.kind == CXTypeKind.CXType_Pointer)
            {
                clangType = clangType.PointeeType;
            }

            name = clangType.Spelling.CString;

            return name;
        }

        private int MapTypeArraySize(CXType clangType)
        {
            if (clangType.TypeClass == CX_TypeClass.CX_TypeClass_ConstantArray)
            {
                return (int)clangType.ArraySize;
            }

            return 0;
        }

        private string MapName(CXCursor clangFunctionParameter)
        {
            return _namesByCursor[clangFunctionParameter];
        }

        private CTypeLayout MapLayout(CXType clangType)
        {
            if (_layoutsByClangType.TryGetValue(clangType, out var layout))
            {
                return layout;
            }

            var clangTypeClass = clangType.TypeClass;
            layout = clangTypeClass switch
            {
                CX_TypeClass.CX_TypeClass_Pointer => MapLayoutForType(clangType),
                CX_TypeClass.CX_TypeClass_Builtin => MapLayoutForType(clangType),
                CX_TypeClass.CX_TypeClass_Enum => MapLayoutForType(clangType),
                CX_TypeClass.CX_TypeClass_Record => MapLayoutForType(clangType),
                CX_TypeClass.CX_TypeClass_Typedef => MapLayoutForType(clangType),
                CX_TypeClass.CX_TypeClass_Elaborated => MapLayoutForType(clangType),
                CX_TypeClass.CX_TypeClass_ConstantArray => MapLayoutForConstArray(clangType),
                CX_TypeClass.CX_TypeClass_FunctionProto => MapLayoutForType(clangType),
                _ => throw new NotImplementedException()
            };

            _layoutsByClangType.Add(clangType, layout);
            return layout;
        }

        private CTypeLayout MapLayoutForType(CXType clangType)
        {
            var size = (int)clangType.SizeOf;
            var alignment = (int)clangType.AlignOf;
            var result = new CTypeLayout(
                size,
                alignment);
            return result;
        }

        private CTypeLayout MapLayoutForConstArray(CXType type)
        {
            var elementLayout = MapLayout(type.ElementType);
            var size = (int)(elementLayout.Size * type.ArraySize);
            var result = new CTypeLayout(
                size,
                elementLayout.Alignment);
            return result;
        }

        private static CLocation MapLocation(CXCursor clangCursor)
        {
            clangCursor.Location.GetFileLocation(
                out var file, out var line, out var column, out var offset);

            if (file.Handle == IntPtr.Zero)
            {
                return default;
            }

            var fileName = Path.GetFileName(file.Name.CString);
            var fileLine = (int)line;
            var fileColumn = (int)column;
            var dateTime = new DateTime(1970, 1, 1).AddSeconds(file.Time);

            var result = new CLocation(
                fileName,
                fileLine,
                fileColumn,
                dateTime);

            return result;
        }
    }
}
