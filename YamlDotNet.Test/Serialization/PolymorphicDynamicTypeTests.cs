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
    public class PolymorphicDynamicTypeTests
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

        public class CustomConvertedShape : Shape
        {
            public string CustomData { get; set; } = string.Empty;
        }

        public class CustomShapeConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => type == typeof(CustomConvertedShape);

            public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                parser.Consume<MappingStart>();
                var key = parser.Consume<Scalar>(); // "CustomData"
                var val = parser.Consume<Scalar>(); // value
                parser.Consume<MappingEnd>();
                return new CustomConvertedShape { Name = "Custom", CustomData = val.Value };
            }

            public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
            {
                var shape = (CustomConvertedShape)value!;
                emitter.Emit(new MappingStart(null, "!" + type.FullName, false, MappingStyle.Block));
                emitter.Emit(new Scalar("CustomData"));
                emitter.Emit(new Scalar(shape.CustomData));
                emitter.Emit(new MappingEnd());
            }
        }

        public class DrawingContainer
        {
            public string Title { get; set; } = string.Empty;
            public List<Shape> Shapes { get; set; } = new List<Shape>();
            public Dictionary<string, Shape> ShapeMap { get; set; } = new Dictionary<string, Shape>();
        }

        [Fact]
        public void PolymorphicList_WithDynamicTypeResolver_RoundtripsCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            var originalList = new List<Shape>
            {
                new Circle { Name = "Circle1", Radius = 5.5 },
                new Rectangle { Name = "Rect1", Width = 10, Height = 20 }
            };

            var yaml = serializer.Serialize(originalList);
            yaml.Should().Contain("Circle");
            yaml.Should().Contain("Radius");
            yaml.Should().Contain("Rectangle");

            var deserialized = deserializer.Deserialize<List<Shape>>(yaml);

            deserialized.Should().HaveCount(2);

            deserialized[0].Should().BeOfType<Circle>();
            var circle = (Circle)deserialized[0];
            circle.Name.Should().Be("Circle1");
            circle.Radius.Should().Be(5.5);

            deserialized[1].Should().BeOfType<Rectangle>();
            var rect = (Rectangle)deserialized[1];
            rect.Name.Should().Be("Rect1");
            rect.Width.Should().Be(10);
            rect.Height.Should().Be(20);
        }

        [Fact]
        public void PolymorphicArray_WithDynamicTypeResolver_RoundtripsCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            Shape[] originalArray = new Shape[]
            {
                new Circle { Name = "CircleA", Radius = 12.3 },
                new Rectangle { Name = "RectB", Width = 4, Height = 8 }
            };

            var yaml = serializer.Serialize(originalArray);
            var deserialized = deserializer.Deserialize<Shape[]>(yaml);

            deserialized.Should().HaveCount(2);
            deserialized[0].Should().BeOfType<Circle>();
            ((Circle)deserialized[0]).Radius.Should().Be(12.3);

            deserialized[1].Should().BeOfType<Rectangle>();
            ((Rectangle)deserialized[1]).Width.Should().Be(4);
        }

        [Fact]
        public void PolymorphicDictionary_WithDynamicTypeResolver_RoundtripsCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            var dict = new Dictionary<string, Shape>
            {
                { "c1", new Circle { Name = "CircleInDict", Radius = 3.14 } },
                { "r1", new Rectangle { Name = "RectInDict", Width = 15, Height = 25 } }
            };

            var yaml = serializer.Serialize(dict);
            var deserialized = deserializer.Deserialize<Dictionary<string, Shape>>(yaml);

            deserialized.Should().HaveCount(2);
            deserialized["c1"].Should().BeOfType<Circle>();
            ((Circle)deserialized["c1"]).Radius.Should().Be(3.14);

            deserialized["r1"].Should().BeOfType<Rectangle>();
            ((Rectangle)deserialized["r1"]).Height.Should().Be(25);
        }

        [Fact]
        public void PolymorphicNestedGraph_WithDynamicTypeResolver_RoundtripsCorrectly()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolver()
                .Build();

            var container = new DrawingContainer
            {
                Title = "My Blueprint",
                Shapes = new List<Shape>
                {
                    new Circle { Name = "C1", Radius = 1.0 }
                },
                ShapeMap = new Dictionary<string, Shape>
                {
                    { "primary", new Rectangle { Name = "R1", Width = 50, Height = 100 } }
                }
            };

            var yaml = serializer.Serialize(container);
            var deserialized = deserializer.Deserialize<DrawingContainer>(yaml);

            deserialized.Title.Should().Be("My Blueprint");
            deserialized.Shapes.Should().HaveCount(1);
            deserialized.Shapes[0].Should().BeOfType<Circle>();
            ((Circle)deserialized.Shapes[0]).Radius.Should().Be(1.0);

            deserialized.ShapeMap.Should().ContainKey("primary");
            deserialized.ShapeMap["primary"].Should().BeOfType<Rectangle>();
            ((Rectangle)deserialized.ShapeMap["primary"]).Width.Should().Be(50);
        }

        [Fact]
        public void CustomTypeConverter_PreservedWithDynamicTypeResolver()
        {
            var converter = new CustomShapeConverter();

            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolver()
                .WithTypeConverter(converter)
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolver()
                .WithTypeConverter(converter)
                .Build();

            var list = new List<Shape>
            {
                new CustomConvertedShape { Name = "Custom", CustomData = "SpecialPayload" }
            };

            var yaml = serializer.Serialize(list);
            yaml.Should().Contain("SpecialPayload");

            var deserialized = deserializer.Deserialize<List<Shape>>(yaml);
            deserialized.Should().HaveCount(1);
            deserialized[0].Should().BeOfType<CustomConvertedShape>();
            ((CustomConvertedShape)deserialized[0]).CustomData.Should().Be("SpecialPayload");
        }

        [Fact]
        public void ManualTagMapping_HasPrecedenceOverDynamicTagging()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolver()
                .WithTagMapping("!my_custom_circle", typeof(Circle))
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolver()
                .WithTagMapping("!my_custom_circle", typeof(Circle))
                .Build();

            var list = new List<Shape>
            {
                new Circle { Name = "TaggedCircle", Radius = 9.9 }
            };

            var yaml = serializer.Serialize(list);
            yaml.Should().Contain("!my_custom_circle");

            var deserialized = deserializer.Deserialize<List<Shape>>(yaml);
            deserialized.Should().HaveCount(1);
            deserialized[0].Should().BeOfType<Circle>();
            ((Circle)deserialized[0]).Radius.Should().Be(9.9);
        }

        [Fact]
        public void DefaultConfiguration_DoesNotEmitDynamicTags()
        {
            var serializer = new SerializerBuilder().Build();

            var list = new List<Rectangle>
            {
                new Rectangle { Name = "StandardRect", Width = 2, Height = 3 }
            };

            var yaml = serializer.Serialize(list);
            yaml.Should().NotContain("!Rectangle");
            yaml.Should().NotContain("!YamlDotNet");
        }
    }
}
