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
using YamlDotNet.Core.Events;

namespace YamlDotNet.Serialization.NodeTypeResolvers
{
    public sealed class TypeNameInTagNodeTypeResolver : INodeTypeResolver
    {
        bool INodeTypeResolver.Resolve(NodeEvent? nodeEvent, ref Type currentType)
        {
            if (nodeEvent != null && !nodeEvent.Tag.IsEmpty)
            {
                var tagValue = nodeEvent.Tag.Value;
                if (tagValue.Length > 0 && tagValue[0] == '!')
                {
                    var rawTypeName = tagValue.Substring(1);
                    var typeName = rawTypeName.Replace("%20", " ");
                    var resolvedType = TryResolveType(typeName);

                    if (resolvedType != null && currentType.IsAssignableFrom(resolvedType))
                    {
                        currentType = resolvedType;
                        return true;
                    }
                }
            }
            return false;
        }

        private static Type? TryResolveType(string typeName)
        {
            var resolvedType = Type.GetType(typeName, throwOnError: false);
            if (resolvedType != null)
            {
                return resolvedType;
            }

            var commaIdx = typeName.IndexOf(',');
            var namePart = commaIdx >= 0 ? typeName.Substring(0, commaIdx) : typeName;
            var asmPart = commaIdx >= 0 ? typeName.Substring(commaIdx) : string.Empty;
            var namePartFix = namePart.Replace(' ', '+');

            if (namePart.Contains(' '))
            {
                resolvedType = Type.GetType(namePartFix + asmPart, throwOnError: false);
                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolvedType = assembly.GetType(namePart, throwOnError: false)
                    ?? assembly.GetType(namePartFix, throwOnError: false);

                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }

            return null;
        }
    }
}
