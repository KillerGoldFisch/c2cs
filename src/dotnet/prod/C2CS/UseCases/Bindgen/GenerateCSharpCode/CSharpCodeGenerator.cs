// Copyright (c) Lucas Girouard-Stranks (https://github.com/lithiumtoast). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory (https://github.com/lithiumtoast/c2cs) for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using C2CS.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace C2CS.Bindgen.GenerateCSharpCode
{
	internal sealed class CSharpCodeGenerator
	{
		private readonly string _libraryName;

		public CSharpCodeGenerator(string libraryName)
		{
			_libraryName = libraryName;
		}

		public ClassDeclarationSyntax CreatePInvokeClass(string name, ImmutableArray<MemberDeclarationSyntax> members)
		{
			var newMembers = new List<MemberDeclarationSyntax>();

			var libraryNameField = FieldDeclaration(
					VariableDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)))
						.WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("LibraryName"))
							.WithInitializer(
								EqualsValueClause(LiteralExpression(
									SyntaxKind.StringLiteralExpression,
									Literal(_libraryName)))))))
				.WithModifiers(
					TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ConstKeyword)));

			newMembers.Add(libraryNameField);
			newMembers.AddRange(members);

			const string comment = @"
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
using System.Runtime.InteropServices;";

			var commentFormatted = comment.TrimStart() + "\r\n";

			var result = ClassDeclaration(name)
				.AddModifiers(
					Token(SyntaxKind.PublicKeyword),
					Token(SyntaxKind.StaticKeyword),
					Token(SyntaxKind.UnsafeKeyword),
					Token(SyntaxKind.PartialKeyword))
				.WithMembers(List(
					newMembers))
				.WithLeadingTrivia(Comment(commentFormatted))
				.Format();

			return result;
		}

		public MethodDeclarationSyntax CreateExternMethod(CSharpFunctionExtern functionExtern)
		{
			var functionName = functionExtern.Name;
			var functionReturnTypeName = functionExtern.ReturnType.Name;
			var functionReturnType = ParseTypeName(functionReturnTypeName);
			var functionCallingConvention = CSharpCallingConvention(functionExtern.CallingConvention);
			var functionParameters = functionExtern.Parameters;

			var cSharpMethod = MethodDeclaration(functionReturnType, functionName)
				.WithDllImportAttribute(functionCallingConvention)
				.WithModifiers(TokenList(
					Token(SyntaxKind.PublicKeyword),
					Token(SyntaxKind.StaticKeyword),
					Token(SyntaxKind.ExternKeyword)))
				.WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

			var parameters = CreateMethodParameters(functionParameters);

			cSharpMethod = cSharpMethod
				.AddParameterListParameters(parameters.ToArray())
				.WithLeadingTrivia(Comment(functionExtern.OriginalCodeLocationComment));

			return cSharpMethod;
		}

		public static MemberDeclarationSyntax CreateFunctionPointer(CSharpFunctionPointer functionPointer)
		{
			var cSharpStruct = StructDeclaration(functionPointer.Name)
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeStructLayout(
					LayoutKind.Explicit, functionPointer.Type.SizeOf, functionPointer.Type.AlignOf);

			var cSharpFieldName = "Pointer";
			var cSharpVariable = VariableDeclarator(Identifier(cSharpFieldName));
			var cSharpFieldType = ParseTypeName("void*");
			var cSharpField = FieldDeclaration(VariableDeclaration(cSharpFieldType)
				.WithVariables(SingletonSeparatedList(cSharpVariable)));

			cSharpField = cSharpField.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(0, functionPointer.Type.SizeOf, 0);

			cSharpStruct = cSharpStruct
				.WithMembers(new SyntaxList<MemberDeclarationSyntax>(cSharpField))
				.WithLeadingTrivia(Comment(functionPointer.OriginalCodeLocationComment));

			return cSharpStruct;
		}

		public EnumDeclarationSyntax CreateEnum(CSharpEnum @enum)
		{
			var cSharpEnumType = @enum.Type.Name;
			var cSharpEnum = EnumDeclaration(@enum.Name)
				.AddModifiers(Token(SyntaxKind.PublicKeyword))
				.AddBaseListTypes(SimpleBaseType(ParseTypeName(cSharpEnumType)));

			var cSharpEnumMembers = ImmutableArray.CreateBuilder<EnumMemberDeclarationSyntax>(@enum.Values.Length);
			foreach (var cEnumValue in @enum.Values)
			{
				var cSharpEqualsValueClause = CreateEnumEqualsValueClause(cEnumValue.Value, cSharpEnumType);
				var cSharpEnumMember = EnumMemberDeclaration(cEnumValue.Name)
					.WithEqualsValue(cSharpEqualsValueClause);

				cSharpEnumMembers.Add(cSharpEnumMember);
			}

			return cSharpEnum.AddMembers(cSharpEnumMembers.ToArray())
				.WithLeadingTrivia(Comment(@enum.OriginalCodeLocationComment));
		}

		public StructDeclarationSyntax CreateStruct(CSharpStruct cSharpStruct)
		{
			var structName = cSharpStruct.Name;
			var structSize = cSharpStruct.Type.SizeOf;
			var structAlignment = cSharpStruct.Type.AlignOf;

			var @struct = StructDeclaration(structName)
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeStructLayout(LayoutKind.Explicit, structSize, structAlignment);

			var structMembers = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();

			foreach (var cSharpField in cSharpStruct.Fields)
			{
				var field = CreateStructField(cSharpField, out var needsWrap);
				structMembers.Add(field);

				if (!needsWrap)
				{
					continue;
				}

				var wrappedMethod = CreateStructFieldWrapperMethod(cSharpStruct.Name, cSharpField);
				structMembers.Add(wrappedMethod);
			}

			foreach (var cSharpStructNested in cSharpStruct.NestedStructs)
			{
				var structNested = CreateStruct(cSharpStructNested);
				structMembers.Add(structNested);
			}

			@struct = @struct
				.AddMembers(structMembers.ToArray())
				.WithLeadingTrivia(Comment(cSharpStruct.OriginalCodeLocationComment));
			return @struct;
		}

		private FieldDeclarationSyntax CreateStructField(
			CSharpStructField cStructField,
			out bool needsWrap)
		{
			FieldDeclarationSyntax result;

			if (cStructField.Type.IsArray)
			{
				result = CreateStructFieldFixedArray(cStructField, out needsWrap);
			}
			else
			{
				result = CreateStructFieldNormal(cStructField);
				needsWrap = false;
			}

			return result;
		}

		private static FieldDeclarationSyntax CreateStructFieldNormal(CSharpStructField cStructField)
		{
			var cSharpFieldName = cStructField.Name;
			var cSharpVariable = VariableDeclarator(Identifier(cSharpFieldName));
			var cSharpFieldType = ParseTypeName(cStructField.Type.Name);
			var cSharpField = FieldDeclaration(VariableDeclaration(cSharpFieldType)
				.WithVariables(SingletonSeparatedList(cSharpVariable)));

			cSharpField = cSharpField.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(cStructField.Offset, cStructField.Type.SizeOf, cStructField.Padding);

			return cSharpField;
		}

		private FieldDeclarationSyntax CreateStructFieldFixedArray(
			CSharpStructField cStructField,
			out bool needsWrap)
		{
			var cSharpFieldName = cStructField.Name;
			var cSharpFieldType = ParseTypeName(cStructField.Type.Name);
			VariableDeclaratorSyntax cSharpVariable;

			var isValidFixedType = IsValidCSharpTypeSpellingForFixedBuffer(cStructField.Type.Name);
			if (isValidFixedType)
			{
				var arraySize = cStructField.Type.SizeOf / cStructField.Type.AlignOf;
				cSharpVariable = VariableDeclarator(Identifier(cSharpFieldName))
					.WithArgumentList(
						BracketedArgumentList(
							SingletonSeparatedList(
								Argument(
									LiteralExpression(
										SyntaxKind.NumericLiteralExpression,
										Literal(arraySize))))));

				needsWrap = false;
			}
			else
			{
				var typeTokenSyntaxKind = cStructField.Type.AlignOf switch
				{
					1 => SyntaxKind.ByteKeyword,
					2 => SyntaxKind.UShortKeyword,
					4 => SyntaxKind.UIntKeyword,
					8 => SyntaxKind.ULongKeyword,
					_ => throw new ArgumentException("Invalid field alignment.")
				};

				cSharpFieldType = PredefinedType(Token(typeTokenSyntaxKind));
				cSharpVariable = VariableDeclarator(Identifier($"_{cStructField.Name}"))
					.WithArgumentList(BracketedArgumentList(SingletonSeparatedList(
						Argument(
							BinaryExpression(
								SyntaxKind.DivideExpression,
								LiteralExpression(
									SyntaxKind.NumericLiteralExpression,
									Literal(cStructField.Type.SizeOf)),
								LiteralExpression(
									SyntaxKind.NumericLiteralExpression,
									Literal(cStructField.Type.AlignOf)))))));

				needsWrap = true;
			}

			var cSharpField = FieldDeclaration(VariableDeclaration(cSharpFieldType)
				.WithVariables(SingletonSeparatedList(cSharpVariable)));

			cSharpField = cSharpField
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(cStructField.Offset, cStructField.Type.SizeOf, cStructField.Padding)
				.AddModifiers(Token(SyntaxKind.FixedKeyword))
				.WithSemicolonToken(Token(TriviaList(), SyntaxKind.SemicolonToken, TriviaList(
					Comment($"/* original type is `{cStructField.Type.OriginalName}` */"))));

			return cSharpField;
		}

		public StructDeclarationSyntax CreateOpaqueStruct(CSharpOpaqueDataType cOpaqueType)
		{
			var cSharpStruct = StructDeclaration(cOpaqueType.Name)
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeStructLayout(LayoutKind.Explicit, cOpaqueType.Type.SizeOf, cOpaqueType.Type.AlignOf);

			var cSharpFieldType = ParseTypeName("IntPtr");
			var cSharpFieldVariable = VariableDeclarator(Identifier("Handle"));
			var cSharpField = FieldDeclaration(
					VariableDeclaration(cSharpFieldType)
						.WithVariables(SingletonSeparatedList(cSharpFieldVariable)))
				.WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
				.WithAttributeFieldOffset(0, cOpaqueType.Type.SizeOf, 0);
			cSharpStruct = cSharpStruct
				.AddMembers(cSharpField)
				.WithLeadingTrivia(Comment(cOpaqueType.OriginalCodeLocationComment));

			return cSharpStruct;
		}

		private ImmutableArray<ParameterSyntax> CreateMethodParameters(ImmutableArray<CSharpFunctionExternParameter> functionParameters)
		{
			var cSharpMethodParameters = ImmutableArray.CreateBuilder<ParameterSyntax>();
			var cSharpMethodParameterNames = new HashSet<string>();

			foreach (var clangFunctionParameter in functionParameters)
			{
				var cSharpMethodParameterName = clangFunctionParameter.Name;

				while (cSharpMethodParameterNames.Contains(cSharpMethodParameterName))
				{
					var numberSuffixMatch = Regex.Match(cSharpMethodParameterName, "\\d$");
					if (numberSuffixMatch.Success)
					{
						var parameterNameWithoutSuffix = cSharpMethodParameterName.Substring(0, numberSuffixMatch.Index);
						cSharpMethodParameterName = ParameterNameUniqueSuffix(parameterNameWithoutSuffix, numberSuffixMatch.Value);
					}
					else
					{
						cSharpMethodParameterName = ParameterNameUniqueSuffix(cSharpMethodParameterName, string.Empty);
					}
				}

				cSharpMethodParameterNames.Add(cSharpMethodParameterName);
				var cSharpMethodParameter = CreateMethodParameter(clangFunctionParameter, cSharpMethodParameterName);
				cSharpMethodParameters.Add(cSharpMethodParameter);
			}

			return cSharpMethodParameters.ToImmutable();

			static string ParameterNameUniqueSuffix(string parameterNameWithoutSuffix, string parameterSuffix)
			{
				if (parameterSuffix == string.Empty)
				{
					return parameterNameWithoutSuffix + "2";
				}

				var parameterSuffixNumber = int.Parse(parameterSuffix, NumberStyles.Integer, CultureInfo.InvariantCulture);
				parameterSuffixNumber += 1;
				var parameterName = parameterNameWithoutSuffix + parameterSuffixNumber;
				return parameterName;
			}
		}

		private ParameterSyntax CreateMethodParameter(CSharpFunctionExternParameter functionParameter, string parameterName)
		{
			var methodParameter = Parameter(Identifier(parameterName));
			if (functionParameter.IsReadOnly)
			{
				methodParameter = methodParameter.WithAttribute("In");
			}

			var typeName = functionParameter.Type.Name;
			var type = ParseTypeName(typeName);
			methodParameter = methodParameter.WithType(type);

			return methodParameter;
		}

		private static EqualsValueClauseSyntax CreateEnumEqualsValueClause(long value, string type)
		{
			// ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
			var literalToken = type switch
			{
				"int" => Literal((int)value),
				"uint" => Literal((uint)value),
				_ => throw new NotImplementedException($"The syntax kind is not yet supported: {type}.")
			};

			return EqualsValueClause(
				LiteralExpression(SyntaxKind.NumericLiteralExpression, literalToken));
		}

		private static MethodDeclarationSyntax CreateStructFieldWrapperMethod(string structName, CSharpStructField cStructField)
		{
			var cSharpMethodName = cStructField.Name;
			var cSharpFieldName = $"_{cStructField.Name}";
			var cSharpStructTypeName = ParseTypeName(structName);
			var cSharpFieldType = ParseTypeName(cStructField.Type.Name);

			var body = Block(SingletonList<StatementSyntax>(FixedStatement(
				VariableDeclaration(PointerType(cSharpStructTypeName))
					.WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("@this"))
						.WithInitializer(EqualsValueClause(
							PrefixUnaryExpression(SyntaxKind.AddressOfExpression, ThisExpression()))))),
				Block(
					LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
						.WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("pointer"))
							.WithInitializer(EqualsValueClause(CastExpression(
								PointerType(cSharpFieldType),
								PrefixUnaryExpression(SyntaxKind.AddressOfExpression, ElementAccessExpression(
										MemberAccessExpression(
											SyntaxKind.PointerMemberAccessExpression,
											IdentifierName("@this"),
											IdentifierName(cSharpFieldName)))
									.WithArgumentList(BracketedArgumentList(SingletonSeparatedList(
										Argument(LiteralExpression(
											SyntaxKind.NumericLiteralExpression,
											Literal(0))))))))))))),
					LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
						.WithVariables(SingletonSeparatedList(VariableDeclarator(
								Identifier("pointerOffset"))
							.WithInitializer(EqualsValueClause(
								IdentifierName("index")))))),
					ReturnStatement(RefExpression(PrefixUnaryExpression(
						SyntaxKind.PointerIndirectionExpression,
						ParenthesizedExpression(BinaryExpression(
							SyntaxKind.AddExpression,
							IdentifierName("pointer"),
							IdentifierName("pointerOffset"))))))))));

			return MethodDeclaration(RefType(cSharpFieldType), cSharpMethodName)
				.WithModifiers(TokenList(
					Token(SyntaxKind.PublicKeyword)))
				.WithParameterList(ParameterList(SingletonSeparatedList(
					Parameter(Identifier("index"))
						.WithType(PredefinedType(Token(SyntaxKind.IntKeyword)))
						.WithDefault(EqualsValueClause(LiteralExpression(
							SyntaxKind.NumericLiteralExpression,
							Literal(0)))))))
				.WithBody(body);
		}

		private static bool IsValidCSharpTypeSpellingForFixedBuffer(string typeString)
		{
			return typeString switch
			{
				"bool" => true,
				"byte" => true,
				"char" => true,
				"short" => true,
				"int" => true,
				"long" => true,
				"sbyte" => true,
				"ushort" => true,
				"uint" => true,
				"ulong" => true,
				"float" => true,
				"double" => true,
				_ => false
			};
		}

		private static CallingConvention CSharpCallingConvention(CSharpFunctionExternCallingConvention callingConvention)
		{
			return callingConvention switch
			{
				CSharpFunctionExternCallingConvention.Unknown => CallingConvention.Winapi,
				CSharpFunctionExternCallingConvention.C => CallingConvention.Cdecl,
				_ => throw new ArgumentOutOfRangeException(nameof(callingConvention), callingConvention, null)
			};
		}
	}
}
