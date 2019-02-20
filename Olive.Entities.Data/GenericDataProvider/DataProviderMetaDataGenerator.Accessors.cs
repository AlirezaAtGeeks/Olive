﻿using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Olive.Entities.Data
{
    static partial class DataProviderMetaDataGenerator
    {
        static IPropertyData[] SetAccessors(this IEnumerable<PropertyData> @this, Type type)
        {
            var list = @this.ToList();

            GenerateClass(type, list.Where(x => x.CustomAccessorClassName.IsEmpty()).ToList());
            UseCustomClass(type, list.Where(x => x.CustomAccessorClassName.HasValue()).ToList());

            return list.ToArray();
        }

        static void UseCustomClass(Type type, List<PropertyData> list) =>
            list.ForEach(p => p.Accessor = CreateCustomAccessorInstance(type, p.CustomAccessorClassName));

        static IPropertyAccessor CreateCustomAccessorInstance(Type type, string name) =>
            (IPropertyAccessor)Activator.CreateInstance(type.Assembly.GetType(name));

        static void GenerateClass(Type type, List<PropertyData> list)
        {
            var code = "";

            list.ForEach(p => code += GetAccessorClass(type, p));

            var accessores = GetAccessorTypes(type, code);

            list.ForEach(p => p.Accessor = accessores[p.PropertyInfo.Name + "Accessor"]);
        }

        static Dictionary<string, IPropertyAccessor> GetAccessorTypes(Type type, string code)
        {
            var sourceCode = $"using System;\r\nusing Olive.Entities;\r\nusing System.Data;{code}";
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
            string assemblyName = Path.GetRandomFileName();
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(IEntity).Assembly.Location),
                MetadataReference.CreateFromFile(type.Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Guid).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Private.CoreLib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Data.Common.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Console.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "netstandard.dll")),
            };

            var compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var stream = new MemoryStream())
            {
                var emitResult = compilation.Emit(stream);

                if (!emitResult.Success)
                    throw new Exception("Failed to create property accessors type.");

                stream.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(stream.ToArray());

                var result = new Dictionary<string, IPropertyAccessor>();

                foreach (var accessorType in assembly.GetTypes())
                    result.Add(accessorType.Name, (IPropertyAccessor)Activator.CreateInstance(accessorType));

                return result;
            }
        }

        static string GetAccessorClass(Type type, PropertyData property)
        {
            var propertyType = property.PropertyInfo.PropertyType;
            var isBlob = propertyType.IsA<Blob>();
            var fullTypeName = $"{type.Namespace}.{type.Name}";

            string GetValuePart()
            {
                if (isBlob) return "new Blob { FileName = value == DBNull.Value ? null : value as string }";

                return $"value == DBNull.Value ? {GetCastingType(propertyType)}null : ".OnlyWhen(type.IsNullable()) +
                    $"{GetCastingType(propertyType)}value";
            }

            string getSetBody()
            {
                if (property.IsOriginalId)
                    return "throw new InvalidOperationException(\"Cannot set the original Id in this way.\");";

                if(property.IsDeleted)
                    return  $@"var obj = ({fullTypeName})entity;
                        if ((Boolean)value)
                            SoftDeleteAttribute.MarkDeleted(obj);
                        else
                            SoftDeleteAttribute.UnMark(obj);";

                return $@"var obj = ({fullTypeName})entity;
                        obj.{property.PropertyInfo.Name} = {GetValuePart()};";
            }

            string GetValuePartFromReader()
            {
                if (isBlob) return "new Blob { FileName = reader.IsDBNull(index) ? null : reader.GetString(index) }";

                return GetGetValueExpression(propertyType);
            }

            string getSetBodyFromReader()
            {
                if (property.IsOriginalId)
                    return "throw new InvalidOperationException(\"Cannot set the original Id in this way.\");";

                if (property.IsDeleted)
                    return $@"var obj = ({fullTypeName})entity;
                        if (reader.GetBoolean(index))
                            SoftDeleteAttribute.MarkDeleted(obj);
                        else
                            SoftDeleteAttribute.UnMark(obj);";

                return $@"var obj = ({fullTypeName})entity;
                        obj.{property.PropertyInfo.Name} = {GetValuePartFromReader()};";
            }

            return $@"
                public class {property.PropertyInfo.Name}Accessor : IPropertyAccessor
                {{
                    public object Get(IEntity entity) => (({fullTypeName})entity).{property.PropertyInfo.Name}{"?.FileName".OnlyWhen(isBlob)};

                    public void Set(IEntity entity, object value)
                    {{
                        {getSetBody()}
                    }}

                    public void Set(IEntity entity, IDataReader reader, int index)
                    {{
                        {getSetBodyFromReader()}
                    }}
                }}";
        }

        static string GetCastingType(Type type, bool ignoreParentheses = false)
        {
            if (type.IsNullable())
                return $"({GetCastingType(type.GenericTypeArguments[0], ignoreParentheses: true)}?)";

            if (type.IsA<double>()) return ignoreParentheses ? $"{type.Name}?)(decimal" : $"({type.Name})(decimal)";

            return ignoreParentheses ? type.Name : $"({type.Name})";
        }

        static string GetGetValueExpression(Type type)
        {
            if (type.IsNullable())
                return "reader.IsDBNull(index) ? (" +
                    GetCastingType(type.GenericTypeArguments[0], ignoreParentheses: true) +
                    $"?)null : {GetGetValueExpression(type.GenericTypeArguments[0])}";

            if (type.IsA<bool>()) return "reader.GetBoolean(index)";
            if (type.IsA<byte>()) return "reader.GetByte(index)";
            if (type.IsA<char>()) return "reader.GetChar(index)";
            if (type.IsA<DateTime>()) return "reader.GetDateTime(index)";
            if (type.IsA<decimal>()) return "reader.GetDecimal(index)";
            if (type.IsA<double>()) return "(double) reader.GetDecimal(index)";
            if (type.IsA<float>()) return "reader.GetFloat(index)";
            if (type.IsA<short>()) return "reader.GetInt16(index)";
            if (type.IsA<int>()) return "reader.GetInt32(index)";
            if (type.IsA<long>()) return "reader.GetInt64(index)";
            if (type.IsA<string>()) return "reader.GetString(index)";
            if (type.IsA<Guid>()) return "reader.GetGuid(index)";

            return $"{GetCastingType(type)} reader[index]";
        }
    }
}
