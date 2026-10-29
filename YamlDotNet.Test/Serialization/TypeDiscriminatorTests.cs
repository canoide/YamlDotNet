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

#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace YamlDotNet.Test.Serialization
{
    public class TypeDiscriminatorTests
    {
        public abstract class Shape
        {
            public string Name { get; set; } = string.Empty;
        }

        public class Circle : Shape
        {
            public double Radius { get; set; }
        }

        public class Rectangle : Shape
        {
            public double Width { get; set; }
            public double Height { get; set; }
        }

        public class DrawingCanvas
        {
            public string Title { get; set; } = string.Empty;
            public Shape MainShape { get; set; } = null!;
            public List<Shape> Shapes { get; set; } = new List<Shape>();
            public Dictionary<string, Shape> NamedShapes { get; set; } = new Dictionary<string, Shape>();
        }

        public class CustomConvertedType
        {
            public string CustomData { get; set; } = string.Empty;
        }

        public class CustomConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => type == typeof(CustomConvertedType);

            public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                var scalar = parser.Consume<Scalar>();
                return new CustomConvertedType { CustomData = scalar.Value };
            }

            public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
            {
                var item = (CustomConvertedType)value!;
                emitter.Emit(new Scalar(item.CustomData));
            }
        }

        [Fact]
        public void StandaloneDerivedObject_EmitsTypeDiscriminatorAsFirstProperty_AndDeserializesCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            Shape shape = new Circle { Name = "MyCircle", Radius = 5.5 };
            var yaml = serializer.Serialize(shape);

            yaml.Should().Contain("$type:");
            yaml.IndexOf("$type:").Should().BeLessThan(yaml.IndexOf("Name:"));

            var deserialized = deserializer.Deserialize<Shape>(yaml);
            deserialized.Should().BeOfType<Circle>();
            var circle = (Circle)deserialized;
            circle.Name.Should().Be("MyCircle");
            circle.Radius.Should().Be(5.5);
        }

        [Fact]
        public void SingleObjectProperty_EmitsTypeDiscriminator_AndDeserializesCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            var canvas = new DrawingCanvas
            {
                Title = "Primary Canvas",
                MainShape = new Rectangle { Name = "MainBox", Width = 10, Height = 20 }
            };

            var yaml = serializer.Serialize(canvas);
            yaml.Should().Contain("$type:");

            var deserialized = deserializer.Deserialize<DrawingCanvas>(yaml);
            deserialized.Title.Should().Be("Primary Canvas");
            deserialized.MainShape.Should().BeOfType<Rectangle>();
            var rect = (Rectangle)deserialized.MainShape;
            rect.Name.Should().Be("MainBox");
            rect.Width.Should().Be(10);
            rect.Height.Should().Be(20);
        }

        [Fact]
        public void CollectionItems_EmitTypeDiscriminators_AndDeserializeCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            var canvas = new DrawingCanvas
            {
                Title = "Collection Canvas",
                Shapes = new List<Shape>
                {
                    new Circle { Name = "C1", Radius = 2.0 },
                    new Rectangle { Name = "R1", Width = 3.0, Height = 4.0 }
                }
            };

            var yaml = serializer.Serialize(canvas);
            yaml.Should().Contain("$type:");

            var deserialized = deserializer.Deserialize<DrawingCanvas>(yaml);
            deserialized.Shapes.Should().HaveCount(2);
            deserialized.Shapes[0].Should().BeOfType<Circle>();
            ((Circle)deserialized.Shapes[0]).Radius.Should().Be(2.0);
            deserialized.Shapes[1].Should().BeOfType<Rectangle>();
            ((Rectangle)deserialized.Shapes[1]).Width.Should().Be(3.0);
        }

        [Fact]
        public void DictionaryEntries_EmitTypeDiscriminators_AndDeserializeCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithTypeDiscriminator()
                .Build();

            var canvas = new DrawingCanvas
            {
                Title = "Dictionary Canvas",
                NamedShapes = new Dictionary<string, Shape>
                {
                    { "first", new Circle { Name = "Circle1", Radius = 1.0 } },
                    { "second", new Rectangle { Name = "Rect1", Width = 2.0, Height = 2.0 } }
                }
            };

            var yaml = serializer.Serialize(canvas);
            yaml.Should().Contain("$type:");

            var deserialized = deserializer.Deserialize<DrawingCanvas>(yaml);
            deserialized.NamedShapes.Should().HaveCount(2);
            deserialized.NamedShapes["first"].Should().BeOfType<Circle>();
            deserialized.NamedShapes["second"].Should().BeOfType<Rectangle>();
        }

        [Fact]
        public void CustomTypeConverters_ArePreservedWhenTypeDiscriminatorIsEnabled()
        {
            var serializer = new SerializerBuilder()
                .WithTypeDiscriminator()
                .WithTypeConverter(new CustomConverter())
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithTypeDiscriminator()
                .WithTypeConverter(new CustomConverter())
                .Build();

            var obj = new CustomConvertedType { CustomData = "HelloCustom" };
            var yaml = serializer.Serialize(obj);
            yaml.Trim().Should().Be("HelloCustom");

            var deserialized = deserializer.Deserialize<CustomConvertedType>(yaml);
            deserialized.CustomData.Should().Be("HelloCustom");
        }

        [Fact]
        public void DefaultBehavior_UnchangedWhenDisabled()
        {
            var serializer = new SerializerBuilder().Build();
            Shape shape = new Circle { Name = "MyCircle", Radius = 5.5 };

            var yaml = serializer.Serialize(shape);
            yaml.Should().NotContain("$type:");
        }
    }
}
