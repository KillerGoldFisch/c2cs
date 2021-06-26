// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using C2CS.Bindgen.Languages.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace C2CS.UseCases.BindgenCSharp
{
	public class CSharpCodeGenerator
	{
		private readonly string _className;
		private readonly string _libraryName;

		public CSharpCodeGenerator(string className, string libraryName)
		{
			_className = className;
			_libraryName = libraryName;
		}

		public string EmitCode(CSharpAbstractSyntaxTree abstractSyntaxTree)
		{
			var builder = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

			EmitVariableProperties(builder, abstractSyntaxTree.VariablesExtern);
			EmitFunctionExterns(builder, abstractSyntaxTree.FunctionExterns);
			EmitFunctionPointers(builder, abstractSyntaxTree.FunctionPointers);
			EmitStructs(builder, abstractSyntaxTree.Structs);
			EmitOpaqueDataTypes(builder, abstractSyntaxTree.OpaqueDataTypes);
			EmitTypedefs(builder, abstractSyntaxTree.Typedefs);
			EmitEnums(builder, abstractSyntaxTree.Enums);

			EmitVirtualTableLoader(builder, abstractSyntaxTree.FunctionExterns, abstractSyntaxTree.VariablesExtern);
			EmitVirtualTableUnloader(builder, abstractSyntaxTree.FunctionExterns, abstractSyntaxTree.VariablesExtern);
			EmitVirtualTable(builder, abstractSyntaxTree.FunctionExterns, abstractSyntaxTree.VariablesExtern);

			var membersToAdd = builder.ToArray();
			var compilationUnit = EmitCompilationUnit(
				_className,
				_libraryName,
				membersToAdd);
			return compilationUnit.ToFullString();
		}

		private static CompilationUnitSyntax EmitCompilationUnit(
			string className,
			string libraryName,
			MemberDeclarationSyntax[] members)
		{
			var code = $@"
//-------------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by the following tool:
//        https://github.com/lithiumtoast/c2cs
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ReSharper disable All
//-------------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

#nullable enable

public static unsafe partial class {className}
{{
    private const string LibraryName = ""{libraryName}"";
	private static IntPtr _libraryHandle;

	public static void LoadApi(string? libraryFilePath = null)
	{{
		UnloadApi();
		if (libraryFilePath == null)
		{{
			var libraryFileNamePrefix = Runtime.LibraryFileNamePrefix;
			var libraryFileNameExtension = Runtime.LibraryFileNameExtension;
			libraryFilePath = $@""{{libraryFileNamePrefix}}{{LibraryName}}{{libraryFileNameExtension}}"";
		}}
		_libraryHandle = Runtime.LibraryLoad(libraryFilePath);
		if (_libraryHandle == IntPtr.Zero) throw new Exception($""Failed to load library: {{libraryFilePath}}"");
		_LoadVirtualTable();
	}}

	public static void UnloadApi()
	{{
		if (_libraryHandle == IntPtr.Zero) return;
		_UnloadVirtualTable();
		Runtime.LibraryUnload(_libraryHandle);
	}}
}}
";

			var syntaxTree = ParseSyntaxTree(code);
			var compilationUnit = syntaxTree.GetCompilationUnitRoot();
			var @class = (ClassDeclarationSyntax)compilationUnit.Members[0];

			var newClass = @class.AddMembers(members);
			var newCompilationUnit = compilationUnit.ReplaceNode(@class, newClass);

			var workspace = new AdhocWorkspace();
			var newCompilationUnitFormatted = (CompilationUnitSyntax)Formatter.Format(newCompilationUnit, workspace);

			return newCompilationUnitFormatted;
		}

		private void EmitVirtualTableLoader(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpFunction> functionExterns,
			ImmutableArray<CSharpVariable> variablesExtern)
		{
			var variableStatementStrings = variablesExtern.Select(x =>
				@$"_virtualTable.{x.Name} = Runtime.LibraryGetExport(_libraryHandle, ""{x.Name}"");");

			var functionStatementStrings = new List<string>();
			foreach (var functionExtern in functionExterns)
			{
				var parameterStrings = functionExtern.Parameters.Select(
						x => x.Type.Name)
					.Append(functionExtern.ReturnType.Name);
				var parameters = string.Join(',', parameterStrings);
				var functionStatementString = @$"_virtualTable.{functionExtern.Name} = (delegate* unmanaged[Cdecl] <{parameters}>)Runtime.LibraryGetExport(_libraryHandle, ""{functionExtern.Name}"");";
				functionStatementStrings.Add(functionStatementString);
			}

			var functionCode = $@"
private static void _LoadVirtualTable()
{{
	#region ""Functions""

	{string.Join('\n', functionStatementStrings)}

	#endregion

	#region ""Variables""

	{string.Join('\n', variableStatementStrings)}

	#endregion
}}
";

			var function = ParseMemberCode<MethodDeclarationSyntax>(functionCode);
			builder.Add(function);
		}

		private void EmitVirtualTableUnloader(ImmutableArray<MemberDeclarationSyntax>.Builder builder, ImmutableArray<CSharpFunction> functionExterns, ImmutableArray<CSharpVariable> variablesExtern)
		{
			var variableStatementStrings = variablesExtern.Select(x =>
				@$"_virtualTable.{x.Name} = IntPtr.Zero;");

			var functionStatementStrings = new List<string>();
			foreach (var functionExtern in functionExterns)
			{
				var parameterStrings = functionExtern.Parameters.Select(
						x => x.Type.Name)
					.Append(functionExtern.ReturnType.Name);
				var parameters = string.Join(',', parameterStrings);
				var functionStatementString = @$"_virtualTable.{functionExtern.Name} = (delegate* unmanaged[Cdecl] <{parameters}>)IntPtr.Zero;";
				functionStatementStrings.Add(functionStatementString);
			}

			var functionCode = $@"
private static void _UnloadVirtualTable()
{{
	#region ""Functions""

	{string.Join('\n', functionStatementStrings)}

	#endregion

	#region ""Variables""

	{string.Join('\n', variableStatementStrings)}

	#endregion
}}
";

			var function = ParseMemberCode<MethodDeclarationSyntax>(functionCode);
			builder.Add(function);
		}

		private static void EmitVirtualTable(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpFunction> functionExterns,
			ImmutableArray<CSharpVariable> variableExterns)
		{
			var functionPointerStrings = new List<string>();
			var variableStrings = new List<string>();

			foreach (var functionExtern in functionExterns)
			{
				var parameterStrings = functionExtern.Parameters.Select(
					x => x.Type.Name)
					.Append(functionExtern.ReturnType.Name);
				var parameters = string.Join(',', parameterStrings);
				var functionPointerString = $@"public delegate* unmanaged[Cdecl] <{parameters}> {functionExtern.Name};";
				functionPointerStrings.Add(functionPointerString);
			}

			foreach (var variableExtern in variableExterns)
			{
				var variableString = $@"public IntPtr {variableExtern.Name};";
				variableStrings.Add(variableString);
			}

			var structCode = $@"
// The virtual table represents a list of pointers to functions or variables which are resolved in a late manner.
//	This allows for flexibility in swapping implementations at runtime.
//	You can think of it in traditional OOP terms in C# as the locations of the virtual methods and/or properties of an object.
public struct _VirtualTable
{{
	#region ""Function Pointers""
	// These pointers hold the locations in the native library where functions are located at runtime.
	// See: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-9.0/function-pointers

	{string.Join('\n', functionPointerStrings)}

	#endregion
	
	#region ""Variables""
	// These pointers hold the locations in the native library where global variables are located at runtime.
	//	The value pointed by these pointers are updated by reading/writing memory.

	{string.Join('\n', variableStrings)}

	#endregion
}}
";
			var virtualTableStruct = ParseMemberCode<StructDeclarationSyntax>(structCode);
			builder.Add(virtualTableStruct);

			var fieldCode = $@"
private static _VirtualTable _virtualTable;
			";
			var virtualTableField = ParseMemberCode<FieldDeclarationSyntax>(fieldCode);
			builder.Add(virtualTableField);
		}

		private static void EmitVariableProperties(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpVariable> variablesExtern)
		{
			foreach (var variableExtern in variablesExtern)
			{
				var method = EmitVariableProperty(variableExtern);
				builder.Add(method);
			}
		}

		private static PropertyDeclarationSyntax EmitVariableProperty(CSharpVariable variable)
		{
			var code = $@"
public static {variable.Type.Name} {variable.Name}
{{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	get => Runtime.ReadMemory<{variable.Type.Name}>(_virtualTable.{variable.Name});
}}
";

			var result = ParseMemberCode<PropertyDeclarationSyntax>(code);
			return result;
		}

		private static void EmitFunctionExterns(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpFunction> functionExterns)
		{
			foreach (var functionExtern in functionExterns)
			{
				// https://github.com/lithiumtoast/c2cs/issues/15
				var shouldIgnore = false;
				foreach (var cSharpFunctionExternParameter in functionExtern.Parameters)
				{
					if (cSharpFunctionExternParameter.Type.Name == "va_list")
					{
						shouldIgnore = true;
						break;
					}
				}

				if (shouldIgnore)
				{
					continue;
				}

				var member = EmitFunctionExtern(functionExtern);
				builder.Add(member);
			}
		}

		private static MethodDeclarationSyntax EmitFunctionExtern(CSharpFunction function)
		{
			var parameterStrings = function.Parameters.Select(
				x => $@"{x.Type.Name} {x.Name}");
			var parameters = string.Join(',', parameterStrings);

			var argumentStrings = function.Parameters.Select(
				x => $"{x.Name}");
			var arguments = string.Join(',', argumentStrings);

			var statement = function.ReturnType.Name == "void" ?
				$"_virtualTable.{function.Name}({arguments});" :
				$"return _virtualTable.{function.Name}({arguments});";

			var code = $@"
{function.CodeLocationComment}
public static {function.ReturnType.Name} {function.Name}({parameters})
{{
	{statement}
}}
";

			var member = ParseMemberCode<MethodDeclarationSyntax>(code);
			return member;
		}

		private static void EmitFunctionPointers(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpFunctionPointer> functionPointers)
		{
			foreach (var functionPointer in functionPointers)
			{
				var member = EmitFunctionPointer(functionPointer);
				builder.Add(member);
			}
		}

		private static StructDeclarationSyntax EmitFunctionPointer(
			CSharpFunctionPointer functionPointer, bool isNested = false)
		{
			var parameterStrings = functionPointer.Parameters
				.Select(x => $"{x.Type}")
				.Append($"{functionPointer.ReturnType.Name}");
			var parameters = string.Join(',', parameterStrings);

			var code = $@"
{functionPointer.CodeLocationComment}
[StructLayout(LayoutKind.Sequential)]
public struct {functionPointer.Name}
{{
	public delegate* unmanaged <{parameters}> Pointer;
}}
";

			if (isNested)
			{
				code = code.Trim();
			}

			var member = ParseMemberCode<StructDeclarationSyntax>(code);
			return member;
		}

		private static void EmitStructs(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpStruct> structs)
		{
			foreach (var @struct in structs)
			{
				var member = EmitStruct(@struct);
				builder.Add(member);
			}
		}

		private static StructDeclarationSyntax EmitStruct(CSharpStruct @struct, bool isNested = false)
		{
			var memberSyntaxes = EmitStructMembers(
				@struct.Name, @struct.Fields, @struct.NestedStructs, @struct.NestedFunctionPointers);
			var memberStrings = memberSyntaxes.Select(x => x.ToFullString());
			var members = string.Join("\n\n", memberStrings);

			var code = $@"
{@struct.CodeLocationComment}
[StructLayout(LayoutKind.Explicit, Size = {@struct.Type.SizeOf}, Pack = {@struct.Type.AlignOf})]
public struct {@struct.Name}
{{
	{members}
}}
";

			if (isNested)
			{
				code = code.Trim();
			}

			var member = ParseMemberCode<StructDeclarationSyntax>(code);
			return member;
		}

		private static MemberDeclarationSyntax[] EmitStructMembers(
			string structName,
			ImmutableArray<CSharpStructField> fields,
			ImmutableArray<CSharpStruct> nestedStructs,
			ImmutableArray<CSharpFunctionPointer> nestedFunctionPointers)
		{
			var builder = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

			foreach (var field in fields)
			{
				if (!field.Type.IsArray)
				{
					var fieldMember = EmitStructField(field);
					builder.Add(fieldMember);
				}
				else
				{
					var fieldMember = EmitStructFieldFixedBuffer(field);
					builder.Add(fieldMember);

					var methodMember = EmitStructFieldFixedBufferProperty(
						structName, field);
					builder.Add(methodMember);
				}
			}

			foreach (var nestedStruct in nestedStructs)
			{
				var syntax = EmitStruct(nestedStruct, true);
				builder.Add(syntax);
			}

			foreach (var nestedFunctionPointer in nestedFunctionPointers)
			{
				var syntax = EmitFunctionPointer(nestedFunctionPointer, true);
				builder.Add(syntax);
			}

			var structMembers = builder.ToArray();
			return structMembers;
		}

		private static FieldDeclarationSyntax EmitStructField(CSharpStructField field)
		{
			var code = $@"
[FieldOffset({field.Offset})] // size = {field.Type.SizeOf}, padding = {field.Padding}
public {field.Type.Name} {field.Name};
".Trim();

			var member = ParseMemberCode<FieldDeclarationSyntax>(code);
			return member;
		}

		private static FieldDeclarationSyntax EmitStructFieldFixedBuffer(
			CSharpStructField field)
		{
			string typeName;

			if (field.IsWrapped)
			{
				typeName = field.Type.AlignOf switch
				{
					1 => "byte",
					2 => "ushort",
					4 => "uint",
					8 => "ulong",
					_ => throw new InvalidOperationException()
				};
			}
			else
			{
				typeName = field.Type.Name;
			}

			var code = $@"
[FieldOffset({field.Offset})] // size = {field.Type.SizeOf}, padding = {field.Padding}
public fixed {typeName} _{field.Name}[{field.Type.SizeOf}/{field.Type.AlignOf}]; // {field.Type.OriginalName}
".Trim();

			var member = ParseMemberCode<FieldDeclarationSyntax>(code);
			return member;
		}

		private static PropertyDeclarationSyntax EmitStructFieldFixedBufferProperty(
			string structName,
			CSharpStructField field)
		{
			string code;

			if (field.Type.Name == "CString")
			{
				code = $@"
public string {field.Name}
{{
	get
	{{
		fixed ({structName}*@this = &this)
		{{
			var pointer = &@this->_{field.Name}[0];
            var cString = new CString(pointer);
            return Runtime.String(cString);
		}}
	}}
}}
".Trim();
			}
			else
			{
				var elementType = field.Type.Name[..^1];
				if (elementType.EndsWith('*'))
				{
					elementType = "IntPtr";
				}

				code = $@"
public Span<{elementType}> {field.Name}
{{
	get
	{{
		fixed ({structName}*@this = &this)
		{{
			var pointer = &@this->_{field.Name}[0];
			var span = new Span<{elementType}>(pointer, {field.Type.ArraySize});
			return span;
		}}
	}}
}}
".Trim();
			}

			var member = ParseMemberCode<PropertyDeclarationSyntax>(code);
			return member;
		}

		private static void EmitOpaqueDataTypes(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpOpaqueType> opaqueDataTypes)
		{
			foreach (var opaqueDataType in opaqueDataTypes)
			{
				var member = EmitOpaqueStruct(opaqueDataType);
				builder.Add(member);
			}
		}

		private static StructDeclarationSyntax EmitOpaqueStruct(CSharpOpaqueType opaqueType)
		{
			var code = $@"
{opaqueType.CodeLocationComment}
[StructLayout(LayoutKind.Sequential)]
public struct {opaqueType.Name}
{{
}}
";

			var member = ParseMemberCode<StructDeclarationSyntax>(code)!;
			return member;
		}

		private static void EmitTypedefs(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpTypedef> typedefs)
		{
			foreach (var typedef in typedefs)
			{
				var member = EmitTypedef(typedef);
				builder.Add(member);
			}
		}

		private static StructDeclarationSyntax EmitTypedef(CSharpTypedef typedef)
		{
			var code = $@"
{typedef.CodeLocationComment}
[StructLayout(LayoutKind.Explicit, Size = {typedef.UnderlyingType.SizeOf}, Pack = {typedef.UnderlyingType.AlignOf})]
public struct {typedef.Name}
{{
	[FieldOffset(0)] // size = {typedef.UnderlyingType.SizeOf}, padding = 0
    public {typedef.UnderlyingType.Name} Data;

	public static implicit operator {typedef.UnderlyingType.Name}({typedef.Name} data) => data.Data;
	public static implicit operator {typedef.Name}({typedef.UnderlyingType.Name} data) => new() {{Data = data}};
}}
";

			var member = ParseMemberCode<StructDeclarationSyntax>(code)!;
			return member;
		}

		private static void EmitEnums(
			ImmutableArray<MemberDeclarationSyntax>.Builder builder,
			ImmutableArray<CSharpEnum> enums)
		{
			foreach (var @enum in enums)
			{
				var member = EmitEnum(@enum);
				builder.Add(member);
			}
		}

		private static EnumDeclarationSyntax EmitEnum(CSharpEnum @enum)
		{
			var values = EmitEnumValues(@enum.IntegerType.Name, @enum.Values);
			var valuesString = values.Select(x => x.ToFullString());
			var members = string.Join(",\n", valuesString);

			var code = $@"
{@enum.CodeLocationComment}
public enum {@enum.Name} : {@enum.IntegerType}
    {{
        {members}
    }}
";

			var member = ParseMemberCode<EnumDeclarationSyntax>(code);
			return member;
		}

		private static EnumMemberDeclarationSyntax[] EmitEnumValues(
			string enumTypeName, ImmutableArray<CSharpEnumValue> values)
		{
			var builder = ImmutableArray.CreateBuilder<EnumMemberDeclarationSyntax>(values.Length);

			foreach (var value in values)
			{
				var enumEqualsValue = EmitEnumEqualsValue(value.Value, enumTypeName);
				var member = EnumMemberDeclaration(value.Name)
					.WithEqualsValue(enumEqualsValue);

				builder.Add(member);
			}

			return builder.ToArray();
		}

		private static EqualsValueClauseSyntax EmitEnumEqualsValue(long value, string enumTypeName)
		{
			var literalToken = enumTypeName switch
			{
				"int" => Literal((int)value),
				"uint" => Literal((uint)value),
				_ => throw new NotImplementedException($"The enum type is not yet supported: {enumTypeName}.")
			};

			return EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, literalToken));
		}

		private static T ParseMemberCode<T>(string memberCode)
			where T : MemberDeclarationSyntax
		{
			var member = ParseMemberDeclaration(memberCode)!;
			if (member is T syntax)
			{
				return syntax;
			}

			var up = new CSharpCodeGenerationException($"Error generating C# code for {typeof(T)}.");
			throw up;
		}

		private class GeneratorUnexpectedException : Exception
		{
			public GeneratorUnexpectedException(CSharpNode data)
				: base(data.CodeLocationComment)
			{
			}
		}
	}
}
