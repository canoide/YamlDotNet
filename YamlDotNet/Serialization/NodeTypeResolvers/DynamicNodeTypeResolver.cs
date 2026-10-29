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
using System.Reflection;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization.TypeResolvers;

namespace YamlDotNet.Serialization.NodeTypeResolvers
{
    public sealed class DynamicNodeTypeResolver : INodeTypeResolver
    {
        private readonly ITypeResolver typeResolver;
        private readonly bool enableDynamicTypeResolution;
        private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

        public DynamicNodeTypeResolver(ITypeResolver typeResolver)
            : this(typeResolver, false)
        {
        }

        public DynamicNodeTypeResolver(ITypeResolver typeResolver, bool enableDynamicTypeResolution)
        {
            this.typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            this.enableDynamicTypeResolution = enableDynamicTypeResolution;
        }

        public bool Resolve(NodeEvent? nodeEvent, ref Type currentType)
        {
            var isDynamic = enableDynamicTypeResolution || typeResolver is DynamicTypeResolver;
            if (!isDynamic)
            {
                return false;
            }

            if (nodeEvent == null || nodeEvent.Tag.IsEmpty)
            {
                return false;
            }

            var tagValue = nodeEvent.Tag.Value;
            if (tagValue.StartsWith("!!", StringComparison.Ordinal))
            {
                return false;
            }

            if (tagValue.StartsWith('!'))
            {
                tagValue = tagValue.Substring(1);
            }

            if (string.IsNullOrEmpty(tagValue))
            {
                return false;
            }

            var searchName = tagValue.Replace(' ', '+');
            var simpleName = searchName;
            var plusIdx = simpleName.LastIndexOf('+');
            if (plusIdx >= 0 && plusIdx < simpleName.Length - 1)
            {
                simpleName = simpleName.Substring(plusIdx + 1);
            }
            else
            {
                var dotIdx = simpleName.LastIndexOf('.');
                if (dotIdx >= 0 && dotIdx < simpleName.Length - 1)
                {
                    simpleName = simpleName.Substring(dotIdx + 1);
                }
            }

            var expectedBaseType = currentType;

            var cacheKey = $"{expectedBaseType.FullName}:{searchName}";

            var resolvedType = TypeCache.GetOrAdd(cacheKey, _ =>
            {
                var type = Type.GetType(searchName, throwOnError: false);
                if (type != null && IsCompatible(type, expectedBaseType))
                {
                    return type;
                }

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = assembly.GetType(searchName, throwOnError: false, ignoreCase: false);
                        if (type != null && IsCompatible(type, expectedBaseType))
                        {
                            return type;
                        }
                    }
                    catch
                    {
                        // Ignore assembly read errors
                    }
                }

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var t in assembly.GetTypes())
                        {
                            if (IsCompatible(t, expectedBaseType))
                            {
                                if (t.FullName == searchName || t.FullName == tagValue || t.Name == simpleName || t.Name == searchName)
                                {
                                    return t;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore types extraction errors
                    }
                }

                return null;
            });

            if (resolvedType != null)
            {
                currentType = resolvedType;
                return true;
            }

            return false;
        }

        private static bool IsCompatible(Type type, Type expectedBaseType)
        {
            return expectedBaseType == typeof(object) || expectedBaseType.IsAssignableFrom(type);
        }
    }
}
