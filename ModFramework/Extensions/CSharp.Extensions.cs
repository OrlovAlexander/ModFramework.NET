﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil.Cil;

namespace Mod.Framework
{
	public static class CSharpExtensions
	{
		/// <summary>
		/// Tries to generate IL for the given CSharp snippet
		/// </summary>
		/// <param name="module"></param>
		/// <param name="csharp"></param>
		/// <param name="references"></param>
		/// <param name="ingoreAccessAssemblies"></param>
		/// <returns></returns>
		public static Mono.Cecil.MethodDefinition TryGetCSharpScript(this Module module,
			string csharp,
			IEnumerable<MetadataReference> references = null,
			IEnumerable<string> ingoreAccessAssemblies = null
		)
		{
			// uses a trick found here for ingnoring accessibility: https://www.strathweb.com/2018/10/no-internalvisibleto-no-problem-bypassing-c-visibility-rules-with-roslyn/
			var ignores = String.Join("\n", ingoreAccessAssemblies.Select(x => $"[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo(\"{x}\")]"));
			var template = @"
				" + ignores + @"

				class Template { public static void Container() {
					" + csharp + @"
				} }


				namespace System.Runtime.CompilerServices
				{
					[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
					public class IgnoresAccessChecksToAttribute : Attribute
					{
						public IgnoresAccessChecksToAttribute(string assemblyName)
						{
							AssemblyName = assemblyName;
						}

						public string AssemblyName { get; }
					}
				}
			";

			var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
				.WithMetadataImportOptions(MetadataImportOptions.All);

			var topLevelBinderFlagsProperty = typeof(CSharpCompilationOptions)
				.GetProperty("TopLevelBinderFlags", BindingFlags.Instance | BindingFlags.NonPublic);
			topLevelBinderFlagsProperty.SetValue(options, (uint)1 << 22);

			var initial = CSharpCompilation.Create($"ModFramework_Generated_{DateTime.UtcNow.Ticks}", references: new[] {
				MetadataReference.CreateFromFile(typeof(object).Assembly.Location), //mscorlib,
				MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime")).Location),
			}.Concat(references), options: options)
				.AddSyntaxTrees(SyntaxFactory.ParseSyntaxTree(template));

			//using (var dll = new MemoryStream())
			//using (var pdb = new MemoryStream())
			var dll = new MemoryStream();
			var pdb = new MemoryStream();
			{
				var result = initial.Emit(dll, pdb);
				if (result.Success)
				{
					dll.Position = 0;
					pdb.Position = 0;

					var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(dll);
					var method = assembly.Type("Template").Method("Container");
					return method;
				}
			}
			return null;
		}
	}
}
