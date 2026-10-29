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
        }

        public bool Deserialize(IParser parser, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value, ObjectDeserializer rootDeserializer)
        {
            if (!parser.TryConsume<MappingStart>(out var _))
            {
                value = null;
                return false;
            }

            // Strip off the nullable & fsharp option type, if present. This is needed for nullable structs.
            var implementationType = Nullable.GetUnderlyingType(expectedType)
                ?? FsharpHelper.GetOptionUnderlyingType(expectedType)
                ?? expectedType;

            var entries = new List<YamlMappingEntry>();
            var consumedProperties = new HashSet<string>(StringComparer.Ordinal);
            var mark = Mark.Empty;

            while (!parser.TryConsume<MappingEnd>(out var mappingEnd))
            {
                var propertyName = parser.Consume<Scalar>();
                if (duplicateKeyChecking && !consumedProperties.Add(propertyName.Value))
                {
                    throw new YamlException(propertyName.Start, propertyName.End, $"Encountered duplicate key {propertyName.Value}");
                }

                var valueEvents = ReadValueEvents(parser);
                entries.Add(new YamlMappingEntry(propertyName, valueEvents));
                mark = propertyName.End;
            }

            var properties = typeInspector.GetProperties(implementationType, null).ToList();
            var constructors = typeInspector.GetConstructors(implementationType).ToList();

            IConstructorDescriptor? bestCtor = null;
            var bestScore = (-1, int.MinValue, -1); // (matchedCount, -defaultedCount, totalParams)
            Dictionary<IParameterDescriptor, YamlMappingEntry>? bestMatchedEntries = null;

            foreach (var ctor in constructors)
            {
                if (ctor.Parameters.Count == 0)
                {
                    continue;
                }

                var claimed = new HashSet<YamlMappingEntry>();
                var matched = new Dictionary<IParameterDescriptor, YamlMappingEntry>();
                var matchedCount = 0;
                var defaultedCount = 0;
                var isViable = true;

                foreach (var param in ctor.Parameters)
                {
                    var entry = FindMatchingEntry(param, entries, properties, claimed);
                    if (entry != null)
                    {
                        claimed.Add(entry);
                        matched[param] = entry;
                        matchedCount++;
                    }
                    else if (param.HasDefaultValue)
                    {
                        defaultedCount++;
                    }
                    else
                    {
                        isViable = false;
                        break;
                    }
                }

                if (isViable)
                {
                    var score = (matchedCount, -defaultedCount, ctor.Parameters.Count);
                    if (score.CompareTo(bestScore) > 0)
                    {
                        bestScore = score;
                        bestCtor = ctor;
                        bestMatchedEntries = matched;
                    }
                }
            }

            var consumedObjectProperties = new HashSet<string>(StringComparer.Ordinal);
            var consumedEntries = new HashSet<YamlMappingEntry>();

            if (bestCtor != null && (bestScore.Item1 > 0 || !constructors.Any(c => c.Parameters.Count == 0)))
            {
                var args = new object?[bestCtor.Parameters.Count];
                for (var i = 0; i < bestCtor.Parameters.Count; i++)
                {
                    var param = bestCtor.Parameters[i];
                    if (bestMatchedEntries != null && bestMatchedEntries.TryGetValue(param, out var entry))
                    {
                        consumedEntries.Add(entry);
                        var comparison = caseInsensitivePropertyMatching ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                        var property = properties.FirstOrDefault(p => string.Equals(p.Name, entry.KeyScalar.Value, comparison) || string.Equals(NormalizeName(p.Name), NormalizeName(param.Name), StringComparison.OrdinalIgnoreCase));
                        if (property != null)
                        {
                            consumedObjectProperties.Add(property.Name);
                        }

                        var valueParser = new BufferedValueParser(entry.ValueEvents);
                        object? paramValue;
                        if (property?.ConverterType != null)
                        {
                            var typeConverterInstance = typeConverters.GetConverterByType(property.ConverterType);
                            paramValue = typeConverterInstance.ReadYaml(valueParser, param.Type, rootDeserializer);
                        }
                        else
                        {
                            paramValue = nestedObjectDeserializer(valueParser, param.Type);
                        }

                        var convertedValue = typeConverter.ChangeType(paramValue, param.Type, enumNamingConvention, typeInspector);
                        if (property != null)
                        {
                            NullCheck(convertedValue, property, entry.KeyScalar);
                        }

                        args[i] = convertedValue;
                    }
                    else if (param.HasDefaultValue)
                    {
                        args[i] = param.DefaultValue;
                    }
                    else if (param.Type.IsValueType && Nullable.GetUnderlyingType(param.Type) == null)
                    {
                        args[i] = Activator.CreateInstance(param.Type);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }

                value = objectFactory.Create(implementationType, args);
            }
            else
            {
                value = objectFactory.Create(implementationType);
            }

            objectFactory.ExecuteOnDeserializing(value);

            foreach (var entry in entries)
            {
                if (consumedEntries.Contains(entry))
                {
                    continue;
                }

                var propertyName = entry.KeyScalar;
                try
                {
                    var property = typeInspector.GetProperty(implementationType, null, propertyName.Value, ignoreUnmatched, caseInsensitivePropertyMatching);
                    if (property == null)
                    {
                        continue;
                    }

                    consumedObjectProperties.Add(property.Name);

                    var valueParser = new BufferedValueParser(entry.ValueEvents);
                    object? propertyValue;
                    if (property.ConverterType != null)
                    {
                        var typeConverterInstance = typeConverters.GetConverterByType(property.ConverterType);
                        propertyValue = typeConverterInstance.ReadYaml(valueParser, property.Type, rootDeserializer);
                    }
                    else
                    {
                        propertyValue = nestedObjectDeserializer(valueParser, property.Type);
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
            }

            if (enforceRequiredProperties)
            {
                var currentProperties = typeInspector.GetProperties(implementationType, value);
                var missingPropertyNames = new List<string>();
                foreach (var property in currentProperties)
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

        private YamlMappingEntry? FindMatchingEntry(
            IParameterDescriptor param,
            List<YamlMappingEntry> entries,
            List<IPropertyDescriptor> properties,
            HashSet<YamlMappingEntry> claimedEntries)
        {
            var comparison = caseInsensitivePropertyMatching ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var paramName = param.Name;
            var normParamName = NormalizeName(paramName);

            foreach (var prop in properties)
            {
                var normPropName = NormalizeName(prop.Name);
                if (string.Equals(normPropName, normParamName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in entries)
                    {
                        if (!claimedEntries.Contains(entry) && string.Equals(entry.KeyScalar.Value, prop.Name, comparison))
                        {
                            return entry;
                        }
                    }
                }
            }

            var appliedParamName = enumNamingConvention != null ? enumNamingConvention.Apply(paramName) : paramName;

            foreach (var entry in entries)
            {
                if (claimedEntries.Contains(entry))
                {
                    continue;
                }

                if (string.Equals(entry.KeyScalar.Value, appliedParamName, comparison) ||
                    string.Equals(entry.KeyScalar.Value, paramName, comparison) ||
                    string.Equals(NormalizeName(entry.KeyScalar.Value), normParamName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static string NormalizeName(string name)
        {
            return name.Replace("_", string.Empty).Replace("-", string.Empty);
        }

        private static List<ParsingEvent> ReadValueEvents(IParser parser)
        {
            var list = new List<ParsingEvent>();
            var depth = 0;
            do
            {
                var next = parser.Consume<ParsingEvent>();
                depth += next.NestingIncrease;
                list.Add(next);
            } while (depth > 0);
            return list;
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

        private sealed class YamlMappingEntry
        {
            public Scalar KeyScalar { get; }
            public IReadOnlyList<ParsingEvent> ValueEvents { get; }

            public YamlMappingEntry(Scalar keyScalar, IReadOnlyList<ParsingEvent> valueEvents)
            {
                KeyScalar = keyScalar;
                ValueEvents = valueEvents;
            }
        }

        private sealed class BufferedValueParser : IParser
        {
            private readonly IReadOnlyList<ParsingEvent> events;
            private int index;

            public BufferedValueParser(IReadOnlyList<ParsingEvent> events)
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
    }
}
