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
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace YamlDotNet.Test.Serialization
{
    public class ConstructorBindingTests
    {
        [Fact]
        public void DeserializesUsingSingleParameterizedConstructor()
        {
            var yaml = @"
name: Laptop
price: 999.99";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var result = deserializer.Deserialize<ImmutableProduct>(yaml);

            Assert.NotNull(result);
            Assert.Equal("Laptop", result.Name);
            Assert.Equal(999.99m, result.Price);
        }

        [Fact]
        public void DynamicSelectionOfMostSuitableConstructorBasedOnYamlKeys()
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            // Scenario 1: Only name provided -> matches 1-arg constructor
            var yaml1 = "name: WidgetA";
            var result1 = deserializer.Deserialize<MultiCtorWidget>(yaml1);
            Assert.Equal("WidgetA", result1.Name);
            Assert.Equal(1, result1.UsedCtorArgCount);

            // Scenario 2: name, width, height provided -> matches 3-arg constructor
            var yaml2 = @"
name: WidgetB
width: 10
height: 20";
            var result2 = deserializer.Deserialize<MultiCtorWidget>(yaml2);
            Assert.Equal("WidgetB", result2.Name);
            Assert.Equal(10, result2.Width);
            Assert.Equal(20, result2.Height);
            Assert.Equal(3, result2.UsedCtorArgCount);

            // Scenario 3: name, width, height, color provided -> matches 4-arg constructor
            var yaml3 = @"
name: WidgetC
width: 15
height: 25
color: Red";
            var result3 = deserializer.Deserialize<MultiCtorWidget>(yaml3);
            Assert.Equal("WidgetC", result3.Name);
            Assert.Equal(15, result3.Width);
            Assert.Equal(25, result3.Height);
            Assert.Equal("Red", result3.Color);
            Assert.Equal(4, result3.UsedCtorArgCount);
        }

        [Fact]
        public void MixedImmutabilityConstructorAndPropertySetters()
        {
            var yaml = @"
id: PROD-100
name: Smartphone
price: 599.99
category: Electronics";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var result = deserializer.Deserialize<MixedProduct>(yaml);

            Assert.NotNull(result);
            Assert.Equal("PROD-100", result.Id); // supplied via constructor
            Assert.Equal("Smartphone", result.Name); // supplied via constructor
            Assert.Equal(599.99m, result.Price); // set via property setter
            Assert.Equal("Electronics", result.Category); // set via property setter
        }

        [Fact]
        public void InheritanceHierarchyWithConstructorChaining()
        {
            var yaml = @"
id: EMP-42
name: Alice
role: Lead Architect";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var result = deserializer.Deserialize<Employee>(yaml);

            Assert.NotNull(result);
            Assert.Equal("EMP-42", result.Id); // from Person base class via base(id)
            Assert.Equal("Alice", result.Name); // from Person base class via base(id, name)
            Assert.Equal("Lead Architect", result.Role); // from Employee class
        }

        [Fact]
        public void DefaultParameterValuesAndOmittedKeys()
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            // Port is omitted, should use default value 8080
            var yaml1 = "host: localhost";
            var result1 = deserializer.Deserialize<ServerConfig>(yaml1);
            Assert.Equal("localhost", result1.Host);
            Assert.Equal(8080, result1.Port);
            Assert.Null(result1.Protocol);

            // Port is provided
            var yaml2 = @"
host: remotehost
port: 9090
protocol: https";
            var result2 = deserializer.Deserialize<ServerConfig>(yaml2);
            Assert.Equal("remotehost", result2.Host);
            Assert.Equal(9090, result2.Port);
            Assert.Equal("https", result2.Protocol);
        }

        [Fact]
        public void NamingConventionsMatchingConstructorParameters()
        {
            // Test UnderscoredNamingConvention
            var yamlUnderscored = @"
first_name: John
last_name: Doe
age_years: 30";

            var deserializerUnderscored = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var user1 = deserializerUnderscored.Deserialize<UserProfile>(yamlUnderscored);
            Assert.Equal("John", user1.FirstName);
            Assert.Equal("Doe", user1.LastName);
            Assert.Equal(30, user1.AgeYears);

            // Test PascalCaseNamingConvention
            var yamlPascal = @"
FirstName: Jane
LastName: Smith
AgeYears: 25";

            var deserializerPascal = new DeserializerBuilder()
                .WithNamingConvention(PascalCaseNamingConvention.Instance)
                .Build();

            var user2 = deserializerPascal.Deserialize<UserProfile>(yamlPascal);
            Assert.Equal("Jane", user2.FirstName);
            Assert.Equal("Smith", user2.LastName);
            Assert.Equal(25, user2.AgeYears);

            // Test HyphenatedNamingConvention
            var yamlHyphenated = @"
first-name: Bob
last-name: Taylor
age-years: 40";

            var deserializerHyphenated = new DeserializerBuilder()
                .WithNamingConvention(HyphenatedNamingConvention.Instance)
                .Build();

            var user3 = deserializerHyphenated.Deserialize<UserProfile>(yamlHyphenated);
            Assert.Equal("Bob", user3.FirstName);
            Assert.Equal("Taylor", user3.LastName);
            Assert.Equal(40, user3.AgeYears);
        }

        [Fact]
        public void ParameterlessClassWorksNormally()
        {
            var yaml = @"
title: Book Title
author: Author Name";

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var result = deserializer.Deserialize<SimpleBook>(yaml);

            Assert.NotNull(result);
            Assert.Equal("Book Title", result.Title);
            Assert.Equal("Author Name", result.Author);
        }

        #region Helper Test Classes

        public class ImmutableProduct
        {
            public string Name { get; }
            public decimal Price { get; }

            public ImmutableProduct(string name, decimal price)
            {
                Name = name;
                Price = price;
            }
        }

        public class MultiCtorWidget
        {
            public string Name { get; }
            public int Width { get; }
            public int Height { get; }
            public string? Color { get; }
            public int UsedCtorArgCount { get; }

            public MultiCtorWidget(string name)
            {
                Name = name;
                UsedCtorArgCount = 1;
            }

            public MultiCtorWidget(string name, int width, int height)
            {
                Name = name;
                Width = width;
                Height = height;
                UsedCtorArgCount = 3;
            }

            public MultiCtorWidget(string name, int width, int height, string color)
            {
                Name = name;
                Width = width;
                Height = height;
                Color = color;
                UsedCtorArgCount = 4;
            }
        }

        public class MixedProduct
        {
            public string Id { get; }
            public string Name { get; }
            public decimal Price { get; set; }
            public string Category { get; set; } = string.Empty;

            public MixedProduct(string id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        public class Person
        {
            public string Id { get; }
            public string Name { get; }

            public Person(string id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        public class Employee : Person
        {
            public string Role { get; }

            public Employee(string id, string name, string role)
                : base(id, name)
            {
                Role = role;
            }
        }

        public class ServerConfig
        {
            public string Host { get; }
            public int Port { get; }
            public string? Protocol { get; }

            public ServerConfig(string host, int port = 8080, string? protocol = null)
            {
                Host = host;
                Port = port;
                Protocol = protocol;
            }
        }

        public class UserProfile
        {
            public string FirstName { get; }
            public string LastName { get; }
            public int AgeYears { get; }

            public UserProfile(string firstName, string lastName, int ageYears)
            {
                FirstName = firstName;
                LastName = lastName;
                AgeYears = ageYears;
            }
        }

        public class SimpleBook
        {
            public string Title { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
        }

        #endregion
    }
}
