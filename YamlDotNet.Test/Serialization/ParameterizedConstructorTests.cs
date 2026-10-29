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

using FluentAssertions;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace YamlDotNet.Test.Serialization
{
    public class ParameterizedConstructorTests
    {
        public record PrimaryConstructorRecord(string Name, int Age);

        public class ImmutablePerson
        {
            public string Name { get; }
            public int Age { get; }

            public ImmutablePerson(string name, int age)
            {
                Name = name;
                Age = age;
            }
        }

        public class AmbiguousConstructors
        {
            public string Name { get; }
            public int Age { get; }
            public string City { get; }

            public AmbiguousConstructors(string name) : this(name, 0, "Unknown") { }

            [YamlConstructor]
            public AmbiguousConstructors(string name, int age, string city)
            {
                Name = name;
                Age = age;
                City = city;
            }

            public AmbiguousConstructors(string name, int age) : this(name, age, "Default") { }
        }

        public class ClassWithDefaultParam
        {
            public string Title { get; }
            public int Count { get; }

            public ClassWithDefaultParam(string title, int count = 42)
            {
                Title = title;
                Count = count;
            }
        }

        public class MixedClass
        {
            public string Id { get; }
            public string Description { get; set; }

            public MixedClass(string id)
            {
                Id = id;
            }
        }

        public class ClassWithAlias
        {
            public string FullName { get; }

            public ClassWithAlias([YamlMember(Alias = "full_name")] string fullName)
            {
                FullName = fullName;
            }
        }

        [Fact]
        public void PrimaryConstructorRecord_IsDeserialized()
        {
            var yaml = "name: Alice\nage: 30\n";
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithParameterizedConstructors()
                .Build();

            var result = deserializer.Deserialize<PrimaryConstructorRecord>(yaml);

            result.Should().NotBeNull();
            result.Name.Should().Be("Alice");
            result.Age.Should().Be(30);
        }

        [Fact]
        public void ImmutableClass_IsDeserialized()
        {
            var yaml = "name: Bob\nage: 25\n";
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithParameterizedConstructors()
                .Build();

            var result = deserializer.Deserialize<ImmutablePerson>(yaml);

            result.Should().NotBeNull();
            result.Name.Should().Be("Bob");
            result.Age.Should().Be(25);
        }

        [Fact]
        public void AmbiguousConstructors_ResolvedViaYamlConstructor()
        {
            var yaml = "name: Charlie\nage: 40\ncity: London\n";
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithParameterizedConstructors()
                .Build();

            var result = deserializer.Deserialize<AmbiguousConstructors>(yaml);

            result.Should().NotBeNull();
            result.Name.Should().Be("Charlie");
            result.Age.Should().Be(40);
            result.City.Should().Be("London");
        }

        [Fact]
        public void OptionalParameter_UsesDefaultValue_WhenKeyMissing()
        {
            var yaml = "title: Overview\n";
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithParameterizedConstructors()
                .Build();

            var result = deserializer.Deserialize<ClassWithDefaultParam>(yaml);

            result.Should().NotBeNull();
            result.Title.Should().Be("Overview");
            result.Count.Should().Be(42);
        }

        [Fact]
        public void OptionalParameter_UsesYamlValue_WhenKeyProvided()
        {
            var yaml = "title: Overview\ncount: 100\n";
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithParameterizedConstructors()
                .Build();

            var result = deserializer.Deserialize<ClassWithDefaultParam>(yaml);

            result.Should().NotBeNull();
            result.Title.Should().Be("Overview");
            result.Count.Should().Be(100);
        }

        [Fact]
        public void MixedClass_SetsConstructorAndMutableProperties()
        {
            var yaml = "id: item-123\ndescription: A test item\n";
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithParameterizedConstructors()
                .Build();

            var result = deserializer.Deserialize<MixedClass>(yaml);

            result.Should().NotBeNull();
            result.Id.Should().Be("item-123");
            result.Description.Should().Be("A test item");
        }

        [Fact]
        public void YamlMemberAlias_OnConstructorParameter_IsRespected()
        {
            var yaml = "full_name: Jane Doe\n";
            var deserializer = new DeserializerBuilder()
                .WithParameterizedConstructors()
                .Build();

            var result = deserializer.Deserialize<ClassWithAlias>(yaml);

            result.Should().NotBeNull();
            result.FullName.Should().Be("Jane Doe");
        }
    }
}
