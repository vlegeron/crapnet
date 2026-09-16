using System.Reflection;
using System.Runtime.Versioning;
using Crapnet.Application.UseCases;
using Crapnet.Domain.Rules;
using Xunit;

namespace Crapnet.Application.Tests;

/// <summary>
/// Guards the dependency rule. Layering survives review but not attrition: one convenient
/// <c>using</c> in the wrong direction is all it takes, and the compiler is happy to allow it.
/// These tests are not, which is why they exist alongside the behavioural ones.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Rule).Assembly;
    private static readonly Assembly Application = typeof(ISessionOrchestrator).Assembly;

    private static string[] CrapnetReferencesOf(Assembly assembly) => assembly
        .GetReferencedAssemblies()
        .Select(reference => reference.Name ?? string.Empty)
        .Where(name => name.StartsWith("Crapnet", StringComparison.Ordinal))
        .OrderBy(name => name)
        .ToArray();

    [Fact]
    public void DomainDependsOnNothingOfOurs()
        => Assert.Empty(CrapnetReferencesOf(Domain));

    [Fact]
    public void ApplicationDependsOnDomainAlone()
        => Assert.Equal(["Crapnet.Domain"], CrapnetReferencesOf(Application));

    [Theory]
    [InlineData("Crapnet.Infrastructure")]
    [InlineData("Crapnet.App")]
    public void TheInnerLayersNeverReachOutwards(string forbidden)
    {
        Assert.DoesNotContain(forbidden, CrapnetReferencesOf(Domain));
        Assert.DoesNotContain(forbidden, CrapnetReferencesOf(Application));
    }

    [Theory]
    [InlineData("Crapnet.Domain")]
    [InlineData("Crapnet.Application")]
    public void TheInnerLayersArePlatformNeutral(string assemblyName)
    {
        // The payoff for the layering: these run on any machine, with no capture driver, no Wi-Fi
        // radio and no Windows. The moment one of them picks up a Windows target framework, the
        // test suite stops being portable and this test says so.
        var assembly = assemblyName == "Crapnet.Domain" ? Domain : Application;
        var framework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? string.Empty;

        Assert.Contains(".NETCoreApp", framework);
        Assert.DoesNotContain("windows", framework, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDomainCarriesNoInfrastructureConcerns()
    {
        // A type in the domain that talks to a driver, a socket or a file is a layering mistake
        // that no reference check would catch, because the BCL is referenced by everyone.
        var offenders = Domain.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Crapnet.Domain", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => IsInfrastructural(method.ReturnType))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsInfrastructural(Type type)
    {
        var name = type.Namespace ?? string.Empty;
        return name.StartsWith("System.IO", StringComparison.Ordinal)
            || name.StartsWith("System.Net.Sockets", StringComparison.Ordinal)
            || name.StartsWith("System.Runtime.InteropServices", StringComparison.Ordinal);
    }
}
