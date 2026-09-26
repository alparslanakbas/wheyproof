using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace IndirimTakip.Api.Caching;

/// <summary>
/// Puts only the query parameters an endpoint ACTUALLY binds into the cache
/// key; the list is derived from each endpoint's own signature.
///
/// <b>WHY (security review, 26 Sept).</b> The policy used
/// <c>SetVaryByQuery("*")</c>: adding <c>?r=&lt;random&gt;</c> opened a new
/// entry on every request and skipped the cache.
/// <c>/api/category-product-counts</c> runs one list query per category on a
/// miss (~9 queries), so a single parameter made every request do that work.
///
/// <b>Why not a hand-written list:</b> the public policy is shared by every
/// endpoint. One combined list wouldn't do — <c>?search=x</c> is valid for
/// other endpoints, so parameterless ones could still be skipped; per-endpoint
/// lists written by hand mean that one forgotten parameter makes
/// <c>page=2</c> get <c>page=1</c>'s response (silent and serious). Derived
/// from the signature, nothing can be forgotten.
///
/// <b>Where we can't be sure, the old behaviour (<c>*</c>):</b> an endpoint
/// taking <c>HttpContext</c> or <c>HttpRequest</c> may read the raw query
/// itself; a type binding itself with <c>BindAsync</c>, or
/// <c>[AsParameters]</c>, hides which keys it reads. Then every parameter stays
/// in the key — skippable, but never a WRONG response.
/// </summary>
public sealed class EndpointQueryKeysPolicy : IOutputCachePolicy
{
    private static readonly StringValues All = new("*");

    // The endpoint set is fixed for the app's lifetime; don't reflect per request.
    private static readonly ConcurrentDictionary<Endpoint, StringValues> Computed = new();

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        context.CacheVaryByRules.QueryKeys = endpoint is null ? All : Computed.GetOrAdd(endpoint, KeysFor);
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    /// <summary>The query parameter names an endpoint binds (tests read this too).</summary>
    internal static StringValues KeysFor(Endpoint endpoint)
    {
        // Minimal API endpoints put the handler's MethodInfo into their metadata.
        if (endpoint.Metadata.GetMetadata<MethodInfo>() is not { } method)
            return All;

        var routeParameters = (endpoint as RouteEndpoint)?.RoutePattern.Parameters
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var keys = new List<string>();
        foreach (var parameter in method.GetParameters())
        {
            var attributes = parameter.GetCustomAttributes(inherit: true);
            if (attributes.OfType<IFromQueryMetadata>().FirstOrDefault() is { } query)
            {
                keys.Add(query.Name ?? parameter.Name!);
                continue;
            }
            if (attributes.OfType<AsParametersAttribute>().Any())
                return All;
            if (attributes.Any(a => a is IFromRouteMetadata or IFromServiceMetadata or IFromHeaderMetadata
                                    or IFromBodyMetadata or IFromFormMetadata))
                continue;

            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (type == typeof(HttpContext) || type == typeof(HttpRequest) || BindsItself(type))
                return All;
            if (routeParameters.Contains(parameter.Name!))
                continue;
            if (BindsFromQuery(type))
                keys.Add(parameter.Name!);
            // Everything else is a DI service or a special type like CancellationToken.
        }
        return new StringValues([.. keys]);
    }

    private static bool BindsItself(Type type) =>
        type.GetMethod("BindAsync", BindingFlags.Public | BindingFlags.Static) is not null;

    // The types a minimal API binds from the query on GET: strings, numbers,
    // enums, anything with a static TryParse (DateTime, Guid...) and arrays of them.
    private static bool BindsFromQuery(Type type)
    {
        var element = type.IsArray ? type.GetElementType()! : type;
        element = Nullable.GetUnderlyingType(element) ?? element;
        if (element == typeof(string) || element == typeof(StringValues) || element.IsPrimitive || element.IsEnum || element == typeof(decimal))
            return true;
        if (element == typeof(CancellationToken))
            return false;
        return element.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.Name == "TryParse");
    }
}
