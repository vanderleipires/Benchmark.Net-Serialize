﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Benchmark.Utils
{
    /// <summary>
    /// copied from https://github.com/graphql-dotnet/graphql-client/blob/master/src/GraphQL.Client.Serializer.SystemTextJson/ImmutableConverter.cs
    /// </summary>
    public class ImmutableConverter : JsonConverter<object>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert.IsPrimitive)
                return false;

            var nullableUnderlyingType = Nullable.GetUnderlyingType(typeToConvert);
            if (nullableUnderlyingType != null && nullableUnderlyingType.IsValueType)
                return false;

            bool result;
            var constructors = typeToConvert.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length != 1)
            {
                result = false;
            }
            else
            {
                var constructor = constructors[0];
                var parameters = constructor.GetParameters();
                var hasParameters = parameters.Length > 0;
                if (hasParameters)
                {
                    var properties = typeToConvert.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    result = true;
                    foreach (var parameter in parameters)
                    {
                        var hasMatchingProperty = properties.Any(p =>
                                                                     NameOfPropertyAndParameter.Matches(p.Name, parameter.Name, typeToConvert.IsAnonymous()));
                        if (!hasMatchingProperty)
                        {
                            result = false;
                            break;
                        }
                    }
                }
                else
                {
                    result = false;
                }
            }

            return result;
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var valueOfProperty = new Dictionary<PropertyInfo, object>();
            var namedPropertiesMapping = GetNamedProperties(options, GetProperties(typeToConvert));
            reader.Read();
            while (true)
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    reader.Read();
                else
                {

                    var jsonPropName = reader.GetString();
                    if (!string.IsNullOrEmpty(jsonPropName))
                    {
                        var normalizedPropName = ConvertAndNormalizeName(jsonPropName, options);
                        if (!namedPropertiesMapping.TryGetValue(normalizedPropName, out var obProp))
                        {
                            reader.Read();
                        }
                        else
                        {
                            var value = JsonSerializer.Deserialize(ref reader, obProp.PropertyType, options);
                            reader.Read();
                            valueOfProperty[obProp] = value;
                        }
                    }
                }
            }

            var ctor = typeToConvert.GetConstructors(BindingFlags.Public | BindingFlags.Instance).First();
            var parameters = ctor.GetParameters();
            var parameterValues = new object[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameterInfo = parameters[index];
                var value = valueOfProperty.FirstOrDefault(prop =>
                                                      NameOfPropertyAndParameter.Matches(prop.Key.Name, parameterInfo.Name, typeToConvert.IsAnonymous())).Value;

                parameterValues[index] = value;
            }

            var instance = ctor.Invoke(parameterValues);
            return instance;
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            var strippedOptions = new JsonSerializerOptions
            {
                AllowTrailingCommas = options.AllowTrailingCommas,
                DefaultBufferSize = options.DefaultBufferSize,
                DictionaryKeyPolicy = options.DictionaryKeyPolicy,
                Encoder = options.Encoder,
                IgnoreNullValues = options.IgnoreNullValues,
                IgnoreReadOnlyProperties = options.IgnoreReadOnlyProperties,
                MaxDepth = options.MaxDepth,
                PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive,
                PropertyNamingPolicy = options.PropertyNamingPolicy,
                ReadCommentHandling = options.ReadCommentHandling,
                WriteIndented = options.WriteIndented
            };
            foreach (var converter in options.Converters)
            {
                if (!(converter is ImmutableConverter))
                    strippedOptions.Converters.Add(converter);
            }

            JsonSerializer.Serialize(writer, value, strippedOptions);
        }

        private static PropertyInfo[] GetProperties(IReflect typeToConvert) => typeToConvert.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static IReadOnlyDictionary<string, PropertyInfo> GetNamedProperties(JsonSerializerOptions options, IEnumerable<PropertyInfo> properties)
        {
            var result = new Dictionary<string, PropertyInfo>();
            foreach (var property in properties)
            {
                string name;
                var nameAttribute = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (nameAttribute != null)
                {
                    name = nameAttribute.Name;
                }
                else
                {
                    name = options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
                }

                var normalizedName = NormalizeName(name, options);
                result.Add(normalizedName, property);
            }

            return result;
        }

        private static string ConvertAndNormalizeName(string name, JsonSerializerOptions options)
        {
            var convertedName = options.PropertyNamingPolicy?.ConvertName(name) ?? name;
            return options.PropertyNameCaseInsensitive ? convertedName.ToLowerInvariant() : convertedName;
        }

        private static string NormalizeName(string name, JsonSerializerOptions options) => options.PropertyNameCaseInsensitive ? name.ToLowerInvariant() : name;
    }

    internal static class NameOfPropertyAndParameter
    {
        public static bool Matches(string propertyName, string parameterName, bool anonymousType)
        {
            if (propertyName is null && parameterName is null)
            {
                return true;
            }

            if (propertyName is null || parameterName is null)
            {
                return false;
            }

            if (anonymousType)
            {
                return propertyName.Equals(parameterName, StringComparison.Ordinal);
            }
            else
            {
                var xRight = propertyName.AsSpan(1);
                var yRight = parameterName.AsSpan(1);
                return char.ToLowerInvariant(propertyName[0]).CompareTo(parameterName[0]) == 0 && xRight.Equals(yRight, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// copied from https://github.com/dahomey-technologies/Dahomey.Json/blob/master/src/Dahomey.Json/Util/TypeExtensions.cs
    /// </summary>
    internal static class TypeExtensions
    {

        public static bool IsAnonymous(this Type type) =>
            type.Namespace == null
            && type.IsSealed
            && type.BaseType == typeof(object)
            && !type.IsPublic
            && type.IsDefined(typeof(CompilerGeneratedAttribute), false);
    }
}
