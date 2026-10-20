#nullable enable
// Shared source: compiled into the ApiSurface tool, the test project and the Imlight verification harness, so all
// three describe an API in exactly the same way. Reflection only; no dependencies.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SpiralLoader.Tooling;

/// <summary>
/// Describes the public (and protected) surface of types as sorted, self-contained text lines, one per type and
/// member, so a baseline file can be diffed line by line. Anything a separately compiled mod can bind to is
/// included: signatures, parameter names and defaults, nullability, constants, and the names of registry keys.
/// </summary>
public static class PublicApiSurface {

    /// <summary>Describes <paramref name="types"/> (public types only) and their public and protected members.</summary>
    public static IReadOnlyList<string> Describe(IEnumerable<Type> types) {
        var lines = new List<string>();
        foreach (var type in types.Where(IsExposed)) {
            DescribeType(type, lines);
        }
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>Describes the exported types of <paramref name="assembly"/> whose namespace starts with <paramref name="namespacePrefix"/>.</summary>
    public static IReadOnlyList<string> Describe(Assembly assembly, string? namespacePrefix = null) {
        var types = assembly.GetExportedTypes()
            .Where(type => namespacePrefix is null || (type.Namespace ?? string.Empty).StartsWith(namespacePrefix, StringComparison.Ordinal));
        return Describe(types);
    }

    private static bool IsExposed(Type type) => type.IsPublic || (type.IsNestedPublic && type.DeclaringType is not null && IsExposed(type.DeclaringType))
        || (type.IsNestedFamily || type.IsNestedFamORAssem) && type.DeclaringType is { IsSealed: false } declaring && IsExposed(declaring);

    private static void DescribeType(Type type, List<string> lines) {
        var name = TypeName(type);
        lines.Add($"{name} :: type {TypeHeader(type)}");

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var nullability = new NullabilityInfoContext();
        foreach (var member in type.GetMembers(flags)) {
            var description = DescribeMember(type, member, nullability);
            if (description is not null) {
                lines.Add($"{name} :: {description}");
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(IsExposed)) {
            DescribeType(nested, lines);
        }
    }

    private static string TypeHeader(Type type) {
        var builder = new StringBuilder();
        builder.Append(type.IsNestedFamily ? "protected " : type.IsNestedFamORAssem ? "protected " : "public ");
        string kind;
        if (type.IsEnum) {
            kind = "enum";
        } else if (typeof(Delegate).IsAssignableFrom(type)) {
            kind = "delegate";
        } else if (type.IsInterface) {
            kind = "interface";
        } else if (type.IsValueType) {
            kind = "struct";
        } else {
            kind = "class";
            if (type.IsAbstract && type.IsSealed) {
                builder.Append("static ");
            } else if (type.IsAbstract) {
                builder.Append("abstract ");
            } else if (type.IsSealed) {
                builder.Append("sealed ");
            }
        }
        builder.Append(kind);

        var inherits = new List<string>();
        if (!type.IsInterface && !type.IsValueType && type.BaseType is { } baseType && baseType != typeof(object)) {
            inherits.Add(TypeName(baseType));
        }
        inherits.AddRange(type.GetInterfaces().Select(TypeName).Order(StringComparer.Ordinal));
        if (type.IsEnum) {
            inherits.Add(TypeName(Enum.GetUnderlyingType(type)));
        }
        if (inherits.Count > 0) {
            builder.Append(" : ").Append(string.Join(", ", inherits));
        }
        if (type.IsDefined(typeof(ObsoleteAttribute), inherit: false)) {
            builder.Append(" [obsolete]");
        }
        if (type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false)) {
            builder.Append(" [flags]");
        }
        return builder.ToString();
    }

    private static string? DescribeMember(Type declaring, MemberInfo member, NullabilityInfoContext nullability) {
        if (member.Name.Contains('<') || member.Name.Contains('$') || member is Type) {
            return null;
        }

        switch (member) {
            case ConstructorInfo constructor when Visibility(constructor.IsPublic, constructor.IsFamily || constructor.IsFamilyOrAssembly) is { } visibility
                                                  && !constructor.IsStatic:
                return $"{visibility} ctor({Parameters(constructor.GetParameters(), nullability)})";

            case MethodInfo method when !method.IsSpecialName && Visibility(method.IsPublic, method.IsFamily || method.IsFamilyOrAssembly) is { } visibility: {
                var modifiers = MethodModifiers(declaring, method);
                var returnType = Format(method.ReturnParameter.ParameterType, nullability.Create(method.ReturnParameter));
                var generics = method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : string.Empty;
                return $"{visibility} {modifiers}method {returnType} {method.Name}{generics}({Parameters(method.GetParameters(), nullability)}){Obsolete(method)}";
            }

            case PropertyInfo property: {
                var getter = property.GetMethod;
                var setter = property.SetMethod;
                var accessor = new[] { getter, setter }.Where(m => m is not null).Select(m => m!)
                    .Where(m => Visibility(m.IsPublic, m.IsFamily || m.IsFamilyOrAssembly) is not null).ToList();
                if (accessor.Count == 0) {
                    return null;
                }
                var visibility = accessor.Any(m => m.IsPublic) ? "public" : "protected";
                var primary = accessor[0];
                var modifiers = MethodModifiers(declaring, primary);
                var required = property.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false)
                               || property.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute")
                    ? "required " : string.Empty;
                var accessors = new StringBuilder("{");
                if (getter is not null && accessor.Contains(getter)) {
                    accessors.Append(getter.IsPublic || !accessor.Any(m => m.IsPublic) ? " get;" : " protected get;");
                }
                if (setter is not null && accessor.Contains(setter)) {
                    var isInit = setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
                    accessors.Append(isInit ? " init;" : setter.IsPublic || !accessor.Any(m => m.IsPublic) ? " set;" : " protected set;");
                }
                accessors.Append(" }");
                var index = property.GetIndexParameters();
                var propertyName = index.Length > 0 ? $"this[{Parameters(index, nullability)}]" : property.Name;
                var value = primary.IsStatic && index.Length == 0 ? StaticValue(property.PropertyType, () => property.GetValue(null)) : string.Empty;
                return $"{visibility} {modifiers}{required}property {Format(property.PropertyType, nullability.Create(property))} {propertyName} {accessors}{value}{Obsolete(property)}";
            }

            case FieldInfo field when !field.IsSpecialName && Visibility(field.IsPublic, field.IsFamily || field.IsFamilyOrAssembly) is { } visibility: {
                if (declaring.IsEnum) {
                    return $"enum-member {field.Name} = {Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}";
                }
                string kind;
                string value = string.Empty;
                if (field.IsLiteral) {
                    kind = "const ";
                    value = " = " + Literal(field.GetRawConstantValue());
                } else {
                    kind = (field.IsStatic ? "static " : string.Empty) + (field.IsInitOnly ? "readonly " : string.Empty);
                    if (field.IsStatic && field.IsInitOnly) {
                        value = StaticValue(field.FieldType, () => field.GetValue(null));
                    }
                }
                return $"{visibility} {kind}field {Format(field.FieldType, nullability.Create(field))} {field.Name}{value}";
            }

            case EventInfo @event when @event.AddMethod is { } add && Visibility(add.IsPublic, add.IsFamily || add.IsFamilyOrAssembly) is { } visibility:
                return $"{visibility} {(add.IsStatic ? "static " : string.Empty)}event {Format(@event.EventHandlerType!, null)} {@event.Name}";

            default:
                return null;
        }
    }

    // Registry key names and version constants are part of the contract and are not visible in a signature.
    private static string StaticValue(Type type, Func<object?> read) {
        var isRegistryKey = type.IsGenericType && type.GetGenericTypeDefinition().FullName == "SpiralLoader.Api.Registries.RegistryKey`1";
        if (!isRegistryKey && type.FullName != "SpiralLoader.Api.Versioning.SemanticVersion") {
            return string.Empty;
        }
        try {
            var value = read();
            if (value is null) {
                return string.Empty;
            }
            var text = isRegistryKey ? (string?)value.GetType().GetProperty("Name")?.GetValue(value) : value.ToString();
            return text is null ? string.Empty : " = " + Literal(text);
        } catch (Exception ex) when (ex is TargetInvocationException or TypeInitializationException) {
            return string.Empty;
        }
    }

    private static string? Visibility(bool isPublic, bool isProtected) => isPublic ? "public" : isProtected ? "protected" : null;

    private static string Obsolete(MemberInfo member) => member.IsDefined(typeof(ObsoleteAttribute), inherit: false) ? " [obsolete]" : string.Empty;

    private static string MethodModifiers(Type declaring, MethodInfo method) {
        var builder = new StringBuilder();
        if (method.IsStatic) {
            builder.Append("static ");
        } else if (declaring.IsInterface) {
            builder.Append(method.IsAbstract ? "abstract " : "default ");
        } else if (method.IsAbstract) {
            builder.Append("abstract ");
        } else if (method.IsVirtual && !method.IsFinal) {
            builder.Append(method.GetBaseDefinition() != method ? "override " : "virtual ");
        }
        return builder.ToString();
    }

    private static string Parameters(ParameterInfo[] parameters, NullabilityInfoContext nullability)
        => string.Join(", ", parameters.Select(parameter => {
            var builder = new StringBuilder();
            if (parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false)) {
                builder.Append("params ");
            }
            if (parameter.IsOut) {
                builder.Append("out ");
            } else if (parameter.IsIn) {
                builder.Append("in ");
            } else if (parameter.ParameterType.IsByRef) {
                builder.Append("ref ");
            }
            builder.Append(Format(parameter.ParameterType, nullability.Create(parameter))).Append(' ').Append(parameter.Name);
            if (parameter.HasDefaultValue) {
                builder.Append(" = ").Append(Literal(parameter.RawDefaultValue));
            }
            return builder.ToString();
        }));

    private static string Literal(object? value) => value switch {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
        bool flag => flag ? "true" : "false",
        char character => "'" + character + "'",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    private static string TypeName(Type type) => Format(type, null);

    private static string Format(Type type, NullabilityInfo? info) {
        if (type.IsByRef) {
            return Format(type.GetElementType()!, info?.ElementType);
        }
        if (type.IsArray) {
            return Format(type.GetElementType()!, info?.ElementType) + "[" + new string(',', type.GetArrayRank() - 1) + "]" + Nullable(type, info);
        }
        if (type.IsGenericParameter) {
            return type.Name + Nullable(type, info);
        }
        if (type.IsGenericType) {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(Nullable<>)) {
                return Format(arguments[0], null) + "?";
            }
            var name = (definition.FullName ?? definition.Name);
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0) {
                name = name[..tick];
            }
            var nested = info?.GenericTypeArguments;
            var formatted = arguments.Select((argument, i) => Format(argument, nested is not null && nested.Length == arguments.Length ? nested[i] : null));
            return $"{name}<{string.Join(", ", formatted)}>{Nullable(type, info)}";
        }
        return (type.FullName ?? type.Name).Replace('+', '.') + Nullable(type, info);
    }

    private static string Nullable(Type type, NullabilityInfo? info)
        => !type.IsValueType && info?.ReadState == NullabilityState.Nullable ? "?" : string.Empty;

}

/// <summary>The difference between a recorded API baseline and the current surface.</summary>
public sealed class ApiComparison {

    public ApiComparison(IReadOnlyList<string> baseline, IReadOnlyList<string> current) {
        // "surface <label>" header lines only name what was described; they are not part of the API.
        baseline = [.. baseline.Where(line => !line.StartsWith("surface ", StringComparison.Ordinal))];
        current = [.. current.Where(line => !line.StartsWith("surface ", StringComparison.Ordinal))];
        var baselineSet = new HashSet<string>(baseline, StringComparer.Ordinal);
        var currentSet = new HashSet<string>(current, StringComparer.Ordinal);
        Removed = [.. baseline.Where(line => !currentSet.Contains(line)).Order(StringComparer.Ordinal)];
        Added = [.. current.Where(line => !baselineSet.Contains(line)).Order(StringComparer.Ordinal)];
    }

    /// <summary>Lines in the baseline that are gone or changed. Any entry here is a potentially breaking change.</summary>
    public IReadOnlyList<string> Removed { get; }

    /// <summary>Lines that are new or changed. Additive on their own; the other half of a change shows up in <see cref="Removed"/>.</summary>
    public IReadOnlyList<string> Added { get; }

    public bool IsIdentical => Removed.Count == 0 && Added.Count == 0;

    /// <summary>True when something a mod could already bind to was removed or changed.</summary>
    public bool IsBreaking => Removed.Count > 0;

    public string Format(string baselineName) {
        var builder = new StringBuilder();
        if (IsIdentical) {
            return $"The API matches {baselineName}.";
        }
        builder.AppendLine(IsBreaking
            ? $"The API differs from {baselineName} in a way that can break existing mods (members removed or changed):"
            : $"The API has grown since {baselineName} (additions only; a minor version bump):");
        foreach (var line in Removed) {
            builder.AppendLine("  - " + line);
        }
        foreach (var line in Added) {
            builder.AppendLine("  + " + line);
        }
        return builder.ToString().TrimEnd();
    }

}
