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
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace YamlDotNet.Test.Serialization
{
    public class PolymorphicCollectionTests
    {
        public class BaseItem
        {
            public string BaseName { get; set; } = string.Empty;
        }

        public class DerivedItem : BaseItem
        {
            public string DerivedProperty { get; set; } = string.Empty;
        }

        public class Container
        {
            public List<BaseItem> Items { get; set; } = new List<BaseItem>();
        }

        public class CustomItemConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => type == typeof(DerivedItem);

            public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) => throw new NotImplementedException();

            public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
            {
                var item = (DerivedItem)value;
                emitter.Emit(new Scalar($"custom_{item.DerivedProperty}"));
            }
        }

        [Fact]
        public void WhenDynamicTypeForGenericCollectionsIsDisabled_EmitsOnlyBaseProperties()
        {
            var container = new Container
            {
                Items = new List<BaseItem>
                {
                    new DerivedItem { BaseName = "Item1", DerivedProperty = "Extra1" }
                }
            };

            var serializer = new SerializerBuilder()
                .WithDynamicTypeForGenericCollections(false)
                .Build();

            var yaml = serializer.Serialize(container);

            yaml.Should().Contain("BaseName: Item1");
            yaml.Should().NotContain("DerivedProperty");
        }

        [Fact]
        public void WhenDynamicTypeForGenericCollectionsIsEnabled_EmitsDerivedProperties()
        {
            var container = new Container
            {
                Items = new List<BaseItem>
                {
                    new DerivedItem { BaseName = "Item1", DerivedProperty = "Extra1" }
                }
            };

            var serializer = new SerializerBuilder()
                .WithDynamicTypeForGenericCollections(true)
                .Build();

            var yaml = serializer.Serialize(container);

            yaml.Should().Contain("BaseName: Item1");
            yaml.Should().Contain("DerivedProperty: Extra1");
        }

        [Fact]
        public void DefaultBehavior_PreservesExistingBehavior_EmitsOnlyBaseProperties()
        {
            var container = new Container
            {
                Items = new List<BaseItem>
                {
                    new DerivedItem { BaseName = "Item1", DerivedProperty = "Extra1" }
                }
            };

            var serializer = new SerializerBuilder().Build();

            var yaml = serializer.Serialize(container);

            yaml.Should().Contain("BaseName: Item1");
            yaml.Should().NotContain("DerivedProperty");
        }

        [Fact]
        public void WhenDynamicTypeIsEnabled_CustomTypeConverterForDerivedTypeIsRespected()
        {
            var container = new Container
            {
                Items = new List<BaseItem>
                {
                    new DerivedItem { BaseName = "Item1", DerivedProperty = "Extra1" }
                }
            };

            var serializer = new SerializerBuilder()
                .WithDynamicTypeForGenericCollections(true)
                .WithTypeConverter(new CustomItemConverter())
                .Build();

            var yaml = serializer.Serialize(container);

            yaml.Should().Contain("custom_Extra1");
        }

        [Fact]
        public void WhenDynamicTypeIsDisabled_CustomTypeConverterForDerivedTypeIsNotInvoked()
        {
            var container = new Container
            {
                Items = new List<BaseItem>
                {
                    new DerivedItem { BaseName = "Item1", DerivedProperty = "Extra1" }
                }
            };

            var serializer = new SerializerBuilder()
                .WithDynamicTypeForGenericCollections(false)
                .WithTypeConverter(new CustomItemConverter())
                .Build();

            var yaml = serializer.Serialize(container);

            yaml.Should().NotContain("custom_Extra1");
            yaml.Should().Contain("BaseName: Item1");
        }

        [Fact]
        public void Deserialization_WithAndWithoutOption_WorksCorrectly()
        {
            var yaml = @"
Items:
- BaseName: Item1
";
            var deserializer = new DeserializerBuilder().Build();
            var result = deserializer.Deserialize<Container>(yaml);

            result.Should().NotBeNull();
            result.Items.Should().HaveCount(1);
            result.Items[0].BaseName.Should().Be("Item1");
        }
    }
}
