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
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Helpers;
using YamlDotNet.Serialization.Utilities;

namespace YamlDotNet.Serialization.NodeDeserializers
{
    public sealed class ObjectNodeDeserializer : INodeDeserializer
    {
        private readonly IObjectFactory objectFactory;
        private readonly ITypeInspector typeInspector;
        private readonly bool ignoreUnmatched;
        private readonly bool duplicateKeyChecking;
        private readonly ITypeConverter typeConverter;
        private readonly INamingConvention enumNamingConvention;
        private readonly bool enforceNullability;
        private readonly bool caseInsensitivePropertyMatching;
        private readonly bool enforceRequiredProperties;
        private readonly TypeConverterCache typeConverters;
        private readonly bool allowParameterizedConstructors;

        public ObjectNodeDeserializer(IObjectFactory objectFactory,
            ITypeInspector typeInspector,
            bool ignoreUnmatched,
            bool duplicateKeyChecking,
            ITypeConverter typeConverter,
            INamingConvention enumNamingConvention,
            bool enforceNullability,
            bool caseInsensitivePropertyMatching,
            bool enforceRequiredProperties,
            IEnumerable<IYamlTypeConverter> typeConverters)
            : this(objectFactory, typeInspector, ignoreUnmatched, duplicateKeyChecking, typeConverter, enumNamingConvention, enforceNullability, caseInsensitivePropertyMatching, enforceRequiredProperties, typeConverters, false)
        {
        }

        public ObjectNodeDeserializer(IObjectFactory objectFactory,
            ITypeInspector typeInspector,
            bool ignoreUnmatched,
            bool duplicateKeyChecking,
            ITypeConverter typeConverter,
            INamingConvention enumNamingConvention,
            bool enforceNullability,
            bool caseInsensitivePropertyMatching,
            bool enforceRequiredProperties,
            IEnumerable<IYamlTypeConverter> typeConverters,
            bool allowParameterizedConstructors)
        {
            this.objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
            this.typeInspector = typeInspector ?? throw new ArgumentNullException(nameof(ObjectNodeDeserializer.typeInspector));
            this.ignoreUnmatched = ignoreUnmatched;
            this.duplicateKeyChecking = duplicateKeyChecking;
            this.typeConverter = typeConverter ?? throw new ArgumentNullException(nameof(typeConverter));
            this.enumNamingConvention = enumNamingConvention ?? throw new ArgumentNullException(nameof(enumNamingConvention));
            this.enforceNullability = enforceNullability;
            this.caseInsensitivePropertyMatching = caseInsensitivePropertyMatching;
            this.enforceRequiredProperties = enforceRequiredProperties;
            this.typeConverters = new TypeConverterCache(typeConverters);
            this.allowParameterizedConstructors = allowParameterizedConstructors;
        }

        public bool Deserialize(IParser parser, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value, ObjectDeserializer rootDeserializer)
        {
            if (!parser.TryConsume<MappingStart>(out var mapping))
            {
                value = null;
                return false;
            }

            // Strip off the nullable & fsharp option type, if present. This is needed for nullable structs.
            var implementationType = Nullable.GetUnderlyingType(expectedType)
                ?? FsharpHelper.GetOptionUnderlyingType(expectedType)
                ?? expectedType;

            var constructors = allowParameterizedConstructors ? typeInspector.GetConstructors(implementationType).ToList() : new List<IConstructorDescriptor>();
            var hasParameterizedConstructors = constructors.Any(c => c.Parameters.Count > 0);

            if (!allowParameterizedConstructors || !hasParameterizedConstructors)
            {
                return DeserializeStandard(parser, implementationType, nestedObjectDeserializer, out value, rootDeserializer);
            }

            return DeserializeWithParameterizedConstructor(parser, implementationType, constructors, nestedObjectDeserializer, out value, rootDeserializer);
        }

        private bool DeserializeStandard(IParser parser, Type implementationType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value, ObjectDeserializer rootDeserializer)
        {
            value = objectFactory.Create(implementationType);
            objectFactory.ExecuteOnDeserializing(value);

            var consumedProperties = new HashSet<string>(StringComparer.Ordinal);
            var consumedObjectProperties = new HashSet<string>(StringComparer.Ordinal);
            var mark = Mark.Empty;

            while (!parser.TryConsume<MappingEnd>(out var _))
            {
                var propertyName = parser.Consume<Scalar>();
                if (duplicateKeyChecking && !consumedProperties.Add(propertyName.Value))
                {
                    throw new YamlException(propertyName.Start, propertyName.End, $"Encountered duplicate key {propertyName.Value}");
                }
                try
                {
                    var property = typeInspector.GetProperty(implementationType, null, propertyName.Value, ignoreUnmatched, caseInsensitivePropertyMatching);
                    if (property == null)
                    {
                        parser.SkipThisAndNestedEvents();
                        continue;
                    }
                    consumedObjectProperties.Add(property.Name);

                    object? propertyValue;
                    if (property.ConverterType != null)
                    {
                        var typeConverterInstance = typeConverters.GetConverterByType(property.ConverterType);
                        propertyValue = typeConverterInstance.ReadYaml(parser, property.Type, rootDeserializer);
                    }
                    else
                    {
                        propertyValue = nestedObjectDeserializer(parser, property.Type);
                    }

                    if (propertyValue is IValuePromise propertyValuePromise)
                    {
                        var valueRef = value;
                        propertyValuePromise.ValueAvailable += v =>
                        {
                            var convertedValue = typeConverter.ChangeType(v, property.Type, enumNamingConvention, typeInspector);

                            NullCheck(convertedValue, property, propertyName);

                            property.Write(valueRef, convertedValue);
                        };
                    }
                    else
                    {
                        var convertedValue = typeConverter.ChangeType(propertyValue, property.Type, enumNamingConvention, typeInspector);

                        NullCheck(convertedValue, property, propertyName);

                        property.Write(value, convertedValue);
                    }
                }
                catch (SerializationException ex)
                {
                    throw new YamlException(propertyName.Start, propertyName.End, ex.Message);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new YamlException(propertyName.Start, propertyName.End, "Exception during deserialization", ex);
                }
                mark = propertyName.End;
            }

            if (enforceRequiredProperties)
            {
                var properties = typeInspector.GetProperties(implementationType, value);
                var missingPropertyNames = new List<string>();
                foreach (var property in properties)
                {
                    if (property.Required && !consumedObjectProperties.Contains(property.Name))
                    {
                        missingPropertyNames.Add(property.Name);
                    }
                }

                if (missingPropertyNames.Count > 0)
                {
                    var propertyNames = string.Join(",", missingPropertyNames);
                    throw new YamlException(mark, mark, $"Missing properties, '{propertyNames}' in source yaml.");
                }
            }

            objectFactory.ExecuteOnDeserialized(value);
            return true;
        }

        private struct BufferedEntry
        {
            public Scalar KeyScalar { get; }
            public string Key { get; }
            public List<ParsingEvent> ValueEvents { get; }

            public BufferedEntry(Scalar keyScalar, string key, List<ParsingEvent> valueEvents)
            {
                KeyScalar = keyScalar;
                Key = key;
                ValueEvents = valueEvents;
            }
        }

        private bool DeserializeWithParameterizedConstructor(
            IParser parser,
            Type implementationType,
            List<IConstructorDescriptor> constructors,
            Func<IParser, Type, object?> nestedObjectDeserializer,
            out object? value,
            ObjectDeserializer rootDeserializer)
        {
            var bufferedEntries = new List<BufferedEntry>();
            var consumedProperties = new HashSet<string>(StringComparer.Ordinal);
            var mark = Mark.Empty;

            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var propertyName = parser.Consume<Scalar>();
                if (duplicateKeyChecking && !consumedProperties.Add(propertyName.Value))
                {
                    throw new YamlException(propertyName.Start, propertyName.End, $"Encountered duplicate key {propertyName.Value}");
                }

                var valueEvents = new List<ParsingEvent>();
                var depth = 0;
                do
                {
                    var next = parser.Consume<ParsingEvent>();
                    depth += next.NestingIncrease;
                    valueEvents.Add(next);
                } while (depth > 0);

                bufferedEntries.Add(new BufferedEntry(propertyName, propertyName.Value, valueEvents));
                mark = propertyName.End;
            }

            var comparison = caseInsensitivePropertyMatching ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            // Find best matching constructor
            IConstructorDescriptor? bestCtor = null;
            int bestMatchCount = -1;
            int bestTotalParamCount = -1;

            foreach (var ctor in constructors)
            {
                if (ctor.Parameters.Count == 0)
                {
                    continue; // Parameterless constructor is fallback
                }

                int matchCount = 0;
                bool allRequiredMatched = true;

                foreach (var param in ctor.Parameters)
                {
                    bool found = bufferedEntries.Any(e => e.Key.Equals(param.Name, comparison));
                    if (found)
                    {
                        matchCount++;
                    }
                    else
                    {
                        var isOptional = (param is IParameterDescriptor pd && pd.HasDefaultValue) || !param.Required;
                        if (!isOptional)
                        {
                            allRequiredMatched = false;
                        }
                    }
                }

                if (!allRequiredMatched && !ctor.IsPreferred)
                {
                    continue;
                }

                if (ctor.IsPreferred)
                {
                    if (bestCtor == null || !bestCtor.IsPreferred || matchCount > bestMatchCount)
                    {
                        bestCtor = ctor;
                        bestMatchCount = matchCount;
                        bestTotalParamCount = ctor.Parameters.Count;
                    }
                }
                else if (bestCtor == null || (!bestCtor.IsPreferred && (matchCount > bestMatchCount || (matchCount == bestMatchCount && ctor.Parameters.Count > bestTotalParamCount))))
                {
                    bestCtor = ctor;
                    bestMatchCount = matchCount;
                    bestTotalParamCount = ctor.Parameters.Count;
                }
            }

            // Fallback to parameterless constructor if no parameterized constructor matched
            if (bestCtor == null)
            {
                value = objectFactory.Create(implementationType);
                objectFactory.ExecuteOnDeserializing(value);
                ApplyBufferedEntriesToInstance(value, implementationType, bufferedEntries, new HashSet<string>(StringComparer.Ordinal), nestedObjectDeserializer, rootDeserializer, mark);
                objectFactory.ExecuteOnDeserialized(value);
                return true;
            }

            var args = new object?[bestCtor.Parameters.Count];
            var consumedKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < bestCtor.Parameters.Count; i++)
            {
                var param = bestCtor.Parameters[i];
                var entryIndex = bufferedEntries.FindIndex(e => e.Key.Equals(param.Name, comparison));

                if (entryIndex >= 0)
                {
                    var entry = bufferedEntries[entryIndex];
                    consumedKeys.Add(entry.Key);

                    object? paramValue;
                    var bufferedParser = new BufferedEventsParser(entry.ValueEvents);

                    if (param.ConverterType != null)
                    {
                        var converter = typeConverters.GetConverterByType(param.ConverterType);
                        paramValue = converter.ReadYaml(bufferedParser, param.Type, rootDeserializer);
                    }
                    else
                    {
                        paramValue = nestedObjectDeserializer(bufferedParser, param.Type);
                    }

                    var convertedValue = typeConverter.ChangeType(paramValue, param.Type, enumNamingConvention, typeInspector);
                    NullCheck(convertedValue, param, entry.KeyScalar);
                    args[i] = convertedValue;
                }
                else if (param is IParameterDescriptor pd && pd.HasDefaultValue)
                {
                    args[i] = pd.DefaultValue;
                }
                else if (param.Type.IsValueType)
                {
                    args[i] = objectFactory.CreatePrimitive(param.Type) ?? Activator.CreateInstance(param.Type);
                }
                else
                {
                    args[i] = null;
                }
            }

            value = objectFactory.Create(implementationType, args);
            objectFactory.ExecuteOnDeserializing(value);

            ApplyBufferedEntriesToInstance(value, implementationType, bufferedEntries, consumedKeys, nestedObjectDeserializer, rootDeserializer, mark);

            objectFactory.ExecuteOnDeserialized(value);
            return true;
        }

        private void ApplyBufferedEntriesToInstance(
            object instance,
            Type implementationType,
            List<BufferedEntry> bufferedEntries,
            HashSet<string> consumedKeys,
            Func<IParser, Type, object?> nestedObjectDeserializer,
            ObjectDeserializer rootDeserializer,
            Mark mark)
        {
            var consumedObjectProperties = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in bufferedEntries)
            {
                if (consumedKeys.Contains(entry.Key))
                {
                    continue;
                }

                try
                {
                    var property = typeInspector.GetProperty(implementationType, null, entry.Key, ignoreUnmatched, caseInsensitivePropertyMatching);
                    if (property == null)
                    {
                        continue;
                    }
                    consumedObjectProperties.Add(property.Name);

                    if (!property.CanWrite)
                    {
                        continue;
                    }

                    object? propertyValue;
                    var bufferedParser = new BufferedEventsParser(entry.ValueEvents);

                    if (property.ConverterType != null)
                    {
                        var typeConverterInstance = typeConverters.GetConverterByType(property.ConverterType);
                        propertyValue = typeConverterInstance.ReadYaml(bufferedParser, property.Type, rootDeserializer);
                    }
                    else
                    {
                        propertyValue = nestedObjectDeserializer(bufferedParser, property.Type);
                    }

                    if (propertyValue is IValuePromise propertyValuePromise)
                    {
                        var instanceRef = instance;
                        propertyValuePromise.ValueAvailable += v =>
                        {
                            var convertedValue = typeConverter.ChangeType(v, property.Type, enumNamingConvention, typeInspector);
                            NullCheck(convertedValue, property, entry.KeyScalar);
                            property.Write(instanceRef, convertedValue);
                        };
                    }
                    else
                    {
                        var convertedValue = typeConverter.ChangeType(propertyValue, property.Type, enumNamingConvention, typeInspector);
                        NullCheck(convertedValue, property, entry.KeyScalar);
                        property.Write(instance, convertedValue);
                    }
                }
                catch (SerializationException ex)
                {
                    throw new YamlException(entry.KeyScalar.Start, entry.KeyScalar.End, ex.Message);
                }
                catch (YamlException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new YamlException(entry.KeyScalar.Start, entry.KeyScalar.End, "Exception during deserialization", ex);
                }
            }

            if (enforceRequiredProperties)
            {
                var properties = typeInspector.GetProperties(implementationType, instance);
                var missingPropertyNames = new List<string>();
                foreach (var property in properties)
                {
                    if (property.Required && !consumedObjectProperties.Contains(property.Name) && !consumedKeys.Contains(property.Name))
                    {
                        missingPropertyNames.Add(property.Name);
                    }
                }

                if (missingPropertyNames.Count > 0)
                {
                    var propertyNames = string.Join(",", missingPropertyNames);
                    throw new YamlException(mark, mark, $"Missing properties, '{propertyNames}' in source yaml.");
                }
            }
        }

        private class BufferedEventsParser : IParser
        {
            private readonly List<ParsingEvent> events;
            private int index;

            public BufferedEventsParser(List<ParsingEvent> events)
            {
                this.events = events;
                this.index = 0;
            }

            public ParsingEvent? Current => index < events.Count ? events[index] : null;

            public bool MoveNext()
            {
                if (index < events.Count)
                {
                    index++;
                    return index < events.Count;
                }
                return false;
            }
        }

        public void NullCheck(object? value, IPropertyDescriptor property, Scalar propertyName)
        {
            if (enforceNullability &&
                value == null &&
                !property.AllowNulls)
            {
#pragma warning disable CA2201 // Do not raise reserved exception types
                throw new YamlException(propertyName.Start, propertyName.End, "Strict nullability enforcement error.", new NullReferenceException("Yaml value is null when target property requires non null values."));
#pragma warning restore CA2201
            }
        }
    }
}
