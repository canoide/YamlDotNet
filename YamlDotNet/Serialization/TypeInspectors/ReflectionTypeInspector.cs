// This file is part of YamlDotNet - A .NET library for YAML.
// Copyright (c) Antoine Aubry and contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Serialization.Utilities;

namespace YamlDotNet.Serialization.TypeInspectors
{
    public abstract class ReflectionTypeInspector : TypeInspectorSkeleton
    {
        public override string GetEnumName(Type enumType, string name)
        {
#if NETSTANDARD2_0_OR_GREATER || NET6_0_OR_GREATER
            foreach (var enumMember in enumType.GetMembers())
            {
                var attribute = enumMember.GetCustomAttribute<EnumMemberAttribute>();
                if (attribute?.Value == name)
                {
                    return enumMember.Name;
                }
            }
#endif
            return name;
        }

        public override string GetEnumValue(object enumValue)
        {
            if (enumValue == null)
            {
                return string.Empty;
            }

            var result = enumValue.ToString();
#if NETSTANDARD2_0_OR_GREATER || NET6_0_OR_GREATER
            var type = enumValue.GetType();
            if (!string.IsNullOrEmpty(result))
            {
                var enumMembers = type.GetMember(result);
                if (enumMembers.Length > 0)
                {
                    var attribute = enumMembers[0].GetCustomAttribute<EnumMemberAttribute>();
                    if (attribute?.Value != null)
                    {
                        result = attribute.Value;
                    }
                }
            }
#endif
            return result!;
        }

        public override bool HasParseMethod(Type type) => type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null) != null;

        public override object? Parse(string value, Type expectedType)
        {
            var method = expectedType.GetMethod("Parse", [typeof(string)]);

            if (method == null)
            {
                throw new InvalidOperationException($"Type '{expectedType.FullName}' does not have a static Parse method.");
            }

            return method.Invoke(null, new object[] { value });
        }

        protected static IEnumerable<IConstructorDescriptor> GetConstructorsInternal(Type type, ITypeResolver typeResolver, bool includeNonPublicProperties)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (includeNonPublicProperties)
            {
                flags |= BindingFlags.NonPublic;
            }

            var constructors = type.GetConstructors(flags);
            var result = new List<IConstructorDescriptor>();

            var allProps = type.GetProperties(flags);

            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                var paramDescriptors = new List<IPropertyDescriptor>();

                foreach (var param in parameters)
                {
                    var matchingProperty = allProps.FirstOrDefault(p =>
                        string.Equals(p.Name, param.Name, StringComparison.OrdinalIgnoreCase));

                    paramDescriptors.Add(new ReflectionParameterDescriptor(param, matchingProperty, typeResolver));
                }

                var isPreferred = ctor.GetCustomAttribute<YamlConstructorAttribute>() != null;
                result.Add(new ConstructorDescriptor(ctor, paramDescriptors, isPreferred));
            }

            return result;
        }

        protected class ReflectionParameterDescriptor : IParameterDescriptor
        {
            private readonly ParameterInfo parameterInfo;
            private readonly PropertyInfo? matchingProperty;
            private readonly ITypeResolver typeResolver;
            private ConcurrentDictionary<Type, Attribute?>? customAttributeCache;

            public ReflectionParameterDescriptor(ParameterInfo parameterInfo, PropertyInfo? matchingProperty, ITypeResolver typeResolver)
            {
                this.parameterInfo = parameterInfo ?? throw new ArgumentNullException(nameof(parameterInfo));
                this.matchingProperty = matchingProperty;
                this.typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
                ScalarStyle = ScalarStyle.Any;

                var converterAttribute = parameterInfo.GetCustomAttribute<YamlConverterAttribute>()
                    ?? matchingProperty?.GetCustomAttribute<YamlConverterAttribute>();
                if (converterAttribute != null)
                {
                    ConverterType = converterAttribute.ConverterType;
                }
            }

            public ParameterInfo ParameterInfo => parameterInfo;
            public string Name => parameterInfo.Name ?? string.Empty;
            public bool Required => !parameterInfo.IsOptional;
            public Type Type => parameterInfo.ParameterType;
            public Type? TypeOverride { get; set; }
            public Type? ConverterType { get; set; }
            public bool AllowNulls => parameterInfo.AcceptsNull();
            public int Order { get; set; }
            public bool CanWrite => true;
            public ScalarStyle ScalarStyle { get; set; }
            public bool HasDefaultValue => parameterInfo.HasDefaultValue;
            public object? DefaultValue => parameterInfo.DefaultValue;

            public void Write(object target, object? value)
            {
                if (matchingProperty != null && matchingProperty.CanWrite)
                {
                    matchingProperty.SetValue(target, value, null);
                }
            }

            public T? GetCustomAttribute<T>() where T : Attribute
            {
                var cache = customAttributeCache ??= new ConcurrentDictionary<Type, Attribute?>();
                if (!cache.TryGetValue(typeof(T), out var attribute))
                {
                    attribute = (T?)parameterInfo.GetAllCustomAttributes<T>().FirstOrDefault()
                        ?? (T?)matchingProperty?.GetAllCustomAttributes<T>().FirstOrDefault();
                    cache[typeof(T)] = attribute;
                }
                return (T?)attribute;
            }

            public IObjectDescriptor Read(object target)
            {
                if (matchingProperty != null && matchingProperty.CanRead)
                {
                    var propertyValue = matchingProperty.ReadValue(target);
                    var actualType = TypeOverride ?? typeResolver.Resolve(Type, propertyValue);
                    return new ObjectDescriptor(propertyValue, actualType, Type, ScalarStyle);
                }
                return new ObjectDescriptor(null, Type, Type, ScalarStyle);
            }
        }
    }
}
