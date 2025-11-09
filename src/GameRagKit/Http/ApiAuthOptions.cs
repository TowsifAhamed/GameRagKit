using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace GameRagKit.Http;

public sealed class ApiAuthOptions
{
    private static readonly string[] DefaultAnonymousPaths = ["/health", "/metrics"]; // allow health & metrics by default

    public ApiAuthOptions(
        ISet<string> apiKeys,
        ISet<string> bearerTokens,
        ISet<string> anonymousPaths)
    {
        ApiKeys = apiKeys.ToImmutableHashSet(StringComparer.Ordinal);
        BearerTokens = bearerTokens.ToImmutableHashSet(StringComparer.Ordinal);
        AnonymousPaths = anonymousPaths
            .Select(NormalizePath)
            .Where(static path => path is not null)
            .Select(static path => path!)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IImmutableSet<string> ApiKeys { get; }

    public IImmutableSet<string> BearerTokens { get; }

    public IImmutableSet<string> AnonymousPaths { get; }

    public bool RequiresAuthentication => ApiKeys.Count > 0 || BearerTokens.Count > 0;

    public bool IsAnonymousPath(PathString path)
    {
        if (!RequiresAuthentication)
        {
            return true;
        }

        if (!path.HasValue)
        {
            return false;
        }

        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return AnonymousPaths.Contains(value);
    }

    public bool TryValidateApiKey(StringValues apiKey)
    {
        if (!RequiresAuthentication)
        {
            return true;
        }

        if (StringValues.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        foreach (var value in apiKey)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (ApiKeys.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryValidateBearer(StringValues authorization)
    {
        if (!RequiresAuthentication)
        {
            return true;
        }

        if (StringValues.IsNullOrEmpty(authorization))
        {
            return false;
        }

        foreach (var header in authorization)
        {
            if (string.IsNullOrEmpty(header))
            {
                continue;
            }

            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = header["Bearer ".Length..];
                if (BearerTokens.Contains(token))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static ApiAuthOptions FromEnvironment()
    {
        var apiKeys = ReadSetFromEnvironment("SERVICE_API_KEYS", "SERVICE_API_KEY");
        var bearerTokens = ReadSetFromEnvironment("SERVICE_BEARER_TOKENS", "SERVICE_BEARER_TOKEN");
        var anonymousPaths = ReadSetFromEnvironment("SERVICE_AUTH_ALLOW", null);

        foreach (var path in DefaultAnonymousPaths)
        {
            anonymousPaths.Add(path);
        }

        return new ApiAuthOptions(apiKeys, bearerTokens, anonymousPaths);
    }

    private static ISet<string> ReadSetFromEnvironment(string listVariable, string? singleVariable)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        var list = Environment.GetEnvironmentVariable(listVariable);
        if (!string.IsNullOrWhiteSpace(list))
        {
            foreach (var item in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                values.Add(item);
            }
        }

        if (!string.IsNullOrWhiteSpace(singleVariable))
        {
            var single = Environment.GetEnvironmentVariable(singleVariable);
            if (!string.IsNullOrWhiteSpace(single))
            {
                values.Add(single);
            }
        }

        return values;
    }

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.StartsWith('/') ? path : "/" + path;
    }
}
