using FluentAssertions;
using GameRagKit.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace GameRagKit.Tests.Http;

public sealed class ApiAuthOptionsTests
{
    [Fact]
    public void RequiresAuthentication_False_When_NoSecretsConfigured()
    {
        var options = new ApiAuthOptions(new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

        options.RequiresAuthentication.Should().BeFalse();
        options.IsAnonymousPath("/anything").Should().BeTrue();
        options.TryValidateApiKey(StringValues.Empty).Should().BeTrue();
        options.TryValidateBearer(StringValues.Empty).Should().BeTrue();
    }

    [Fact]
    public void AnonymousPaths_AreNormalized_And_FilterOutBlanks()
    {
        var options = new ApiAuthOptions(
            new HashSet<string> { "alpha" },
            new HashSet<string>(),
            new HashSet<string> { string.Empty, "health", "/metrics" });

        options.AnonymousPaths.Should().BeEquivalentTo(new[] { "/health", "/metrics" });
        options.IsAnonymousPath(new PathString("/metrics")).Should().BeTrue();
        options.IsAnonymousPath(new PathString("/other")).Should().BeFalse();
    }

    [Fact]
    public void TryValidateApiKey_Ignores_Blank_Values()
    {
        var options = new ApiAuthOptions(new HashSet<string> { "secret" }, new HashSet<string>(), new HashSet<string>());

        options.TryValidateApiKey(new StringValues(new[] { string.Empty, "secret" })).Should().BeTrue();
        options.TryValidateApiKey(new StringValues(string.Empty)).Should().BeFalse();
    }

    [Fact]
    public void TryValidateBearer_Requires_Bearer_Prefix()
    {
        var options = new ApiAuthOptions(new HashSet<string>(), new HashSet<string> { "token" }, new HashSet<string>());

        options.TryValidateBearer(new StringValues("token")).Should().BeFalse();
        options.TryValidateBearer(new StringValues("Bearer token")).Should().BeTrue();
    }

    [Fact]
    public void FromEnvironment_Falls_Back_To_Defaults()
    {
        const string apiKeysVar = "SERVICE_API_KEYS";
        const string bearerVar = "SERVICE_BEARER_TOKENS";
        const string allowVar = "SERVICE_AUTH_ALLOW";

        try
        {
            Environment.SetEnvironmentVariable(apiKeysVar, "key1,key2");
            Environment.SetEnvironmentVariable(bearerVar, "tok1");
            Environment.SetEnvironmentVariable(allowVar, "health,/custom");

            var options = ApiAuthOptions.FromEnvironment();

            options.ApiKeys.Should().BeEquivalentTo(new[] { "key1", "key2" });
            options.BearerTokens.Should().BeEquivalentTo(new[] { "tok1" });
            options.AnonymousPaths.Should().BeEquivalentTo(new[] { "/health", "/custom", "/metrics" });
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeysVar, null);
            Environment.SetEnvironmentVariable(bearerVar, null);
            Environment.SetEnvironmentVariable(allowVar, null);
        }
    }
}
