#nullable enable

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
    public class DynamicTypeResolutionTests
    {
        public interface IAnimal
        {
            string Name { get; set; }
        }

        public class Dog : IAnimal
        {
            public string Name { get; set; } = string.Empty;
            public int BarkVolume { get; set; }
        }

        public class Cat : IAnimal
        {
            public string Name { get; set; } = string.Empty;
            public bool IsLazy { get; set; }
        }

        public abstract class Shape
        {
            public string Color { get; set; } = string.Empty;
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

        public class AnimalOwner
        {
            public IAnimal Pet { get; set; } = null!;
        }

        public class ShapeContainer
        {
            public Shape MainShape { get; set; } = null!;
        }

        public class Zoo
        {
            public List<IAnimal> Animals { get; set; } = new();
            public IAnimal[] AnimalArray { get; set; } = Array.Empty<IAnimal>();
            public Dictionary<string, IAnimal> NamedAnimals { get; set; } = new();
        }

        public class Company
        {
            public string Name { get; set; } = string.Empty;
            public Manager Manager { get; set; } = null!;
        }

        public class Manager
        {
            public string Title { get; set; } = string.Empty;
            public IAnimal OfficePet { get; set; } = null!;
            public Shape OfficeLayout { get; set; } = null!;
        }

        [Fact]
        public void PolymorphicSingleObjectProperty_WithDynamicTypeResolution_SerializesAndDeserializesConcreteMembers()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var owner = new AnimalOwner
            {
                Pet = new Dog { Name = "Rover", BarkVolume = 11 }
            };

            var yaml = serializer.Serialize(owner);
            yaml.Should().Contain("Rover");
            yaml.Should().Contain("BarkVolume");

            var result = deserializer.Deserialize<AnimalOwner>(yaml);
            result.Pet.Should().BeOfType<Dog>();

            var dog = (Dog)result.Pet;
            dog.Name.Should().Be("Rover");
            dog.BarkVolume.Should().Be(11);
        }

        [Fact]
        public void PolymorphicBaseClassProperty_WithDynamicTypeResolution_SerializesAndDeserializesConcreteMembers()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var container = new ShapeContainer
            {
                MainShape = new Rectangle { Color = "Red", Width = 10, Height = 20 }
            };

            var yaml = serializer.Serialize(container);
            yaml.Should().Contain("Color: Red");
            yaml.Should().Contain("Width: 10");
            yaml.Should().Contain("Height: 20");

            var result = deserializer.Deserialize<ShapeContainer>(yaml);
            result.MainShape.Should().BeOfType<Rectangle>();

            var rect = (Rectangle)result.MainShape;
            rect.Color.Should().Be("Red");
            rect.Width.Should().Be(10);
            rect.Height.Should().Be(20);
        }

        [Fact]
        public void PolymorphicSingleObjectProperty_WhenDisabled_RetainsDefaultBehavior()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolution(false)
                .Build();

            var owner = new AnimalOwner
            {
                Pet = new Dog { Name = "Rover", BarkVolume = 11 }
            };

            var yaml = serializer.Serialize(owner);
            yaml.Should().NotContain("BarkVolume");
        }

        [Fact]
        public void PolymorphicCollections_ListsArraysAndDictionaries_WithDynamicTypeResolution()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var zoo = new Zoo
            {
                Animals = new List<IAnimal>
                {
                    new Dog { Name = "Rex", BarkVolume = 8 },
                    new Cat { Name = "Whiskers", IsLazy = true }
                },
                AnimalArray = new IAnimal[]
                {
                    new Cat { Name = "Felix", IsLazy = false },
                    new Dog { Name = "Spot", BarkVolume = 5 }
                },
                NamedAnimals = new Dictionary<string, IAnimal>
                {
                    { "primary", new Dog { Name = "Buddy", BarkVolume = 12 } },
                    { "secondary", new Cat { Name = "Garfield", IsLazy = true } }
                }
            };

            var yaml = serializer.Serialize(zoo);

            var result = deserializer.Deserialize<Zoo>(yaml);

            result.Animals.Should().HaveCount(2);
            result.Animals[0].Should().BeOfType<Dog>();
            ((Dog)result.Animals[0]).BarkVolume.Should().Be(8);
            result.Animals[1].Should().BeOfType<Cat>();
            ((Cat)result.Animals[1]).IsLazy.Should().BeTrue();

            result.AnimalArray.Should().HaveCount(2);
            result.AnimalArray[0].Should().BeOfType<Cat>();
            ((Cat)result.AnimalArray[0]).IsLazy.Should().BeFalse();
            result.AnimalArray[1].Should().BeOfType<Dog>();
            ((Dog)result.AnimalArray[1]).BarkVolume.Should().Be(5);

            result.NamedAnimals.Should().HaveCount(2);
            result.NamedAnimals["primary"].Should().BeOfType<Dog>();
            ((Dog)result.NamedAnimals["primary"]).BarkVolume.Should().Be(12);
            result.NamedAnimals["secondary"].Should().BeOfType<Cat>();
            ((Cat)result.NamedAnimals["secondary"]).IsLazy.Should().BeTrue();
        }

        [Fact]
        public void PolymorphicNestedStructures_WithDynamicTypeResolution()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolution()
                .Build();

            var company = new Company
            {
                Name = "Acme Corp",
                Manager = new Manager
                {
                    Title = "CEO",
                    OfficePet = new Cat { Name = "Mitten", IsLazy = false },
                    OfficeLayout = new Circle { Color = "Blue", Radius = 5.5 }
                }
            };

            var yaml = serializer.Serialize(company);

            var result = deserializer.Deserialize<Company>(yaml);
            result.Name.Should().Be("Acme Corp");
            result.Manager.Title.Should().Be("CEO");

            result.Manager.OfficePet.Should().BeOfType<Cat>();
            var cat = (Cat)result.Manager.OfficePet;
            cat.Name.Should().Be("Mitten");
            cat.IsLazy.Should().BeFalse();

            result.Manager.OfficeLayout.Should().BeOfType<Circle>();
            var circle = (Circle)result.Manager.OfficeLayout;
            circle.Color.Should().Be("Blue");
            circle.Radius.Should().Be(5.5);
        }

        [Fact]
        public void ManualTagMappings_AreRespectedWithDynamicTypeResolution()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolution()
                .WithTagMapping("!dog", typeof(Dog))
                .WithTagMapping("!cat", typeof(Cat))
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolution()
                .WithTagMapping("!dog", typeof(Dog))
                .WithTagMapping("!cat", typeof(Cat))
                .Build();

            var owner = new AnimalOwner
            {
                Pet = new Dog { Name = "Fido", BarkVolume = 7 }
            };

            var yaml = serializer.Serialize(owner);
            yaml.Should().Contain("!dog");
            yaml.Should().NotContain(typeof(Dog).AssemblyQualifiedName!);

            var result = deserializer.Deserialize<AnimalOwner>(yaml);
            result.Pet.Should().BeOfType<Dog>();
            ((Dog)result.Pet).Name.Should().Be("Fido");
            ((Dog)result.Pet).BarkVolume.Should().Be(7);
        }

        public class CustomAnimalConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => typeof(IAnimal).IsAssignableFrom(type);

            public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                parser.Consume<MappingStart>();
                var key = parser.Consume<Scalar>();
                var val = parser.Consume<Scalar>();
                parser.Consume<MappingEnd>();
                return new Dog { Name = val.Value + "_custom", BarkVolume = 99 };
            }

            public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
            {
                var dog = (Dog)value!;
                emitter.Emit(new MappingStart());
                emitter.Emit(new Scalar("Name"));
                emitter.Emit(new Scalar(dog.Name));
                emitter.Emit(new MappingEnd());
            }
        }

        [Fact]
        public void CustomTypeConverters_WorkWithoutInterference()
        {
            var serializer = new SerializerBuilder()
                .WithDynamicTypeResolution()
                .WithTypeConverter(new CustomAnimalConverter())
                .Build();

            var deserializer = new DeserializerBuilder()
                .WithDynamicTypeResolution()
                .WithTypeConverter(new CustomAnimalConverter())
                .Build();

            var owner = new AnimalOwner
            {
                Pet = new Dog { Name = "CustomDog", BarkVolume = 10 }
            };

            var yaml = serializer.Serialize(owner);
            yaml.Should().Contain("Name: CustomDog");

            var result = deserializer.Deserialize<AnimalOwner>(yaml);
            result.Pet.Should().BeOfType<Dog>();
            ((Dog)result.Pet).Name.Should().Be("CustomDog_custom");
            ((Dog)result.Pet).BarkVolume.Should().Be(99);
        }
    }
}
