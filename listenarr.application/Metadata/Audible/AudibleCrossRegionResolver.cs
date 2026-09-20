/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Metadata.Audible;

public sealed class AudibleRegionalVariant
{
    public string Region { get; set; } = string.Empty;
    public string Asin { get; set; } = string.Empty;
    public string? Sku { get; set; }
}

public sealed class AudibleCrossRegionResolution
{
    public string SourceAsin { get; set; } = string.Empty;
    public string SourceRegion { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? SkuGroup { get; set; }
    public List<AudibleRegionalVariant> Variants { get; set; } = new();
}

public sealed class AudibleCrossRegionResolver
{
    private const int DiscoveryPageSize = 50;
    private const int MaxTitleDiscoveryPages = 10;
    private const string DirectSearchResponseGroups =
        "media,contributors,series,product_attrs,product_desc,product_extended_attrs,category_ladders";

    private static readonly HttpClient DirectSearchHttpClient = new();

    private readonly Func<string, string, bool, string?, Task<AudibleBookResponse?>> _getBookMetadataAsync;
    private readonly Func<string, int, int, string, string?, Task<AudibleSearchResponse?>> _searchByTitleAsync;
    private readonly Func<string, string, int, int, string, string?, Task<AudibleSearchResponse?>> _searchByTitleAndAuthorAsync;
    private readonly ILogger _logger;

    public AudibleCrossRegionResolver(AudibleService audibleService, ILogger logger)
        : this(
            audibleService.GetBookMetadataAsync,
            audibleService.SearchByTitleAsync,
            SearchByTitleAndAuthorDirectAsync,
            logger)
    {
    }

    internal AudibleCrossRegionResolver(
        Func<string, string, bool, string?, Task<AudibleBookResponse?>> getBookMetadataAsync,
        Func<string, int, int, string, string?, Task<AudibleSearchResponse?>> searchByTitleAsync,
        Func<string, string, int, int, string, string?, Task<AudibleSearchResponse?>> searchByTitleAndAuthorAsync,
        ILogger logger)
    {
        _getBookMetadataAsync = getBookMetadataAsync;
        _searchByTitleAsync = searchByTitleAsync;
        _searchByTitleAndAuthorAsync = searchByTitleAndAuthorAsync;
        _logger = logger;
    }

    public async Task<AudibleCrossRegionResolution?> ResolveAsync(
        string asin,
        string sourceRegion,
        IEnumerable<string> targetRegions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(asin))
        {
            throw new ArgumentException("ASIN is required", nameof(asin));
        }

        var normalizedSourceRegion = AudibleRequestHelper.NormalizeRegion(sourceRegion);
        if (!AudibleRequestHelper.IsSupportedRegion(normalizedSourceRegion))
        {
            throw new ArgumentException($"Unsupported Audible region: {sourceRegion}", nameof(sourceRegion));
        }

        var source = await _getBookMetadataAsync(asin.Trim(), normalizedSourceRegion, false, null).ConfigureAwait(false);
        if (source == null)
        {
            return null;
        }

        var resolution = new AudibleCrossRegionResolution
        {
            SourceAsin = source.Asin ?? asin.Trim(),
            SourceRegion = normalizedSourceRegion,
            Title = source.Title,
            SkuGroup = source.SkuGroup
        };

        AddVariant(
            resolution.Variants,
            normalizedSourceRegion,
            source.Asin ?? asin.Trim(),
            source.Sku);

        if (string.IsNullOrWhiteSpace(source.SkuGroup) || string.IsNullOrWhiteSpace(source.Title))
        {
            _logger.LogDebug(
                "Skipping Audible cross-region discovery for {Asin} in {Region}: source metadata has no SKU group or title",
                source.Asin ?? asin,
                normalizedSourceRegion);
            return resolution;
        }

        var normalizedTargets = (targetRegions ?? Enumerable.Empty<string>())
            .Where(region => !string.IsNullOrWhiteSpace(region))
            .Select(AudibleRequestHelper.NormalizeRegion)
            .Where(AudibleRequestHelper.IsSupportedRegion)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(region => !string.Equals(region, normalizedSourceRegion, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var targetRegion in normalizedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var exactMatches = await DiscoverExactMatchesAsync(source, targetRegion, cancellationToken).ConfigureAwait(false);
                foreach (var candidate in exactMatches)
                {
                    AddVariant(
                        resolution.Variants,
                        targetRegion,
                        candidate.Asin!,
                        candidate.Sku);
                }

                _logger.LogDebug(
                    "Audible cross-region discovery for SKU group {SkuGroup} found {Count} exact match(es) in {Region}",
                    source.SkuGroup,
                    exactMatches.Count,
                    targetRegion);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(
                    ex,
                    "Audible cross-region discovery failed for SKU group {SkuGroup} in region {Region}",
                    source.SkuGroup,
                    targetRegion);
            }
        }

        return resolution;
    }

    private async Task<List<AudibleSearchResult>> DiscoverExactMatchesAsync(
        AudibleBookResponse source,
        string targetRegion,
        CancellationToken cancellationToken)
    {
        var author = source.Authors?
            .Select(item => item?.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        if (!string.IsNullOrWhiteSpace(author))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authorSearch = await _searchByTitleAndAuthorAsync(
                source.Title!,
                author!,
                1,
                DiscoveryPageSize,
                targetRegion,
                null).ConfigureAwait(false);

            var authorMatches = FindExactSkuGroupMatches(authorSearch, source.SkuGroup!);
            if (authorMatches.Count > 0)
            {
                _logger.LogDebug(
                    "Audible cross-region discovery found SKU group {SkuGroup} in {Region} using direct title+author catalog search",
                    source.SkuGroup,
                    targetRegion);
                return authorMatches;
            }
        }

        var matches = new List<AudibleSearchResult>();
        for (var page = 1; page <= MaxTitleDiscoveryPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var search = await _searchByTitleAsync(
                source.Title!,
                page,
                DiscoveryPageSize,
                targetRegion,
                null).ConfigureAwait(false);

            matches.AddRange(FindExactSkuGroupMatches(search, source.SkuGroup!));
            if (matches.Count > 0)
            {
                break;
            }

            var resultCount = search?.Results?.Count ?? 0;
            var totalResults = search?.TotalResults;
            if (resultCount == 0 ||
                (totalResults.HasValue && page * DiscoveryPageSize >= totalResults.Value) ||
                resultCount < DiscoveryPageSize)
            {
                break;
            }
        }

        return matches
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Asin))
            .GroupBy(candidate => candidate.Asin!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static async Task<AudibleSearchResponse?> SearchByTitleAndAuthorDirectAsync(
        string title,
        string author,
        int page,
        int limit,
        string region,
        string? language)
    {
        var safeRegion = AudibleRequestHelper.NormalizeRegion(region);
        var parameters = new Dictionary<string, string?>
        {
            ["title"] = title?.Trim(),
            ["author"] = author?.Trim(),
            ["num_results"] = Math.Clamp(limit, 1, 50).ToString(),
            ["page"] = Math.Max(0, page - 1).ToString(),
            ["products_sort_by"] = "Title",
            ["response_groups"] = DirectSearchResponseGroups
        };

        var url = $"{AudibleRequestHelper.BuildApiBaseUrl(safeRegion)}/1.0/catalog/products/?{AudibleRequestHelper.BuildQueryString(parameters)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 15); com.audible.application");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Accept-Charset", "utf-8");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await DirectSearchHttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
            var root = document.RootElement;

            var results = root.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array
                ? products.EnumerateArray()
                    .Where(product => product.ValueKind == JsonValueKind.Object)
                    .Select(product => AudibleProductMapper.MapProductToBookResponse(product, safeRegion))
                    .Where(product => product != null)
                    .Select(product => AudibleProductMapper.MapBookResponseToSearchResult(product!))
                    .Where(product => product != null)
                    .Cast<AudibleSearchResult>()
                    .Where(product => !AudibleSearchResultFilter.IndicatesPodcast(product))
                    .ToList()
                : new List<AudibleSearchResult>();

            results = AudibleProductMapper.ApplyLanguageFilter(results, language);
            var totalResults = root.TryGetProperty("total_results", out var totalResultsElement) &&
                               totalResultsElement.TryGetInt32(out var parsedTotalResults)
                ? parsedTotalResults
                : results.Count;

            return new AudibleSearchResponse
            {
                Results = results,
                TotalResults = totalResults
            };
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static List<AudibleSearchResult> FindExactSkuGroupMatches(
        AudibleSearchResponse? search,
        string skuGroup)
    {
        return search?.Results?
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Asin) &&
                string.Equals(candidate.SkuGroup, skuGroup, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<AudibleSearchResult>();
    }

    private static void AddVariant(
        ICollection<AudibleRegionalVariant> variants,
        string region,
        string asin,
        string? sku)
    {
        if (variants.Any(existing =>
            string.Equals(existing.Region, region, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Asin, asin, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        variants.Add(new AudibleRegionalVariant
        {
            Region = region,
            Asin = asin,
            Sku = sku
        });
    }
}
