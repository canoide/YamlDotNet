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
using YamlDotNet.Core;

namespace YamlDotNet.Serialization
{
    public sealed class TypeDiscriminatorPropertyDescriptor : IPropertyDescriptor
    {
        private readonly Type objectType;

        public TypeDiscriminatorPropertyDescriptor(Type objectType)
        {
            this.objectType = objectType ?? throw new ArgumentNullException(nameof(objectType));
            Name = "$type";
            ScalarStyle = ScalarStyle.Plain;
        }

        public string Name { get; }
        public bool AllowNulls => false;
        public bool CanWrite => true;
        public Type Type => typeof(string);
        public Type? TypeOverride { get; set; }
        public int Order { get; set; } = int.MinValue;
        public ScalarStyle ScalarStyle { get; set; }
        public bool Required => false;
        public Type? ConverterType { get; set; }

        public T? GetCustomAttribute<T>() where T : Attribute
        {
            return null;
        }

        public IObjectDescriptor Read(object target)
        {
            var actualType = target != null ? target.GetType() : objectType;
            var typeName = actualType.AssemblyQualifiedName ?? actualType.FullName ?? actualType.Name;
            return new ObjectDescriptor(typeName, typeof(string), typeof(string), ScalarStyle);
        }

        public void Write(object target, object? value)
        {
            // The $type property is read-only on the underlying target object.
        }
    }
}
