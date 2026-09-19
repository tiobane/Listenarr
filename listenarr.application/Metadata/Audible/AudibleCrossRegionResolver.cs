/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

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
    private readonly Func<string, string, bool, string?, Task<AudibleBookResponse?>> _getBookMetadataAsync;
    private readonly Func<string, int, int, string, string?, Task<AudibleSearchResponse?>> _searchByTitleAsync;
    private readonly ILogger _logger;

    public AudibleCrossRegionResolver(AudibleService audibleService, ILogger logger)
        : this(audibleService.GetBookMetadataAsync, audibleService.SearchByTitleAsync, logger)
    {
    }

    internal AudibleCrossRegionResolver(
        Func<string, string, bool, string?, Task<AudibleBookResponse?>> getBookMetadataAsync,
        Func<string, int, int, string, string?, Task<AudibleSearchResponse?>> searchByTitleAsync,
        ILogger logger)
    {
        _getBookMetadataAsync = getBookMetadataAsync;
        _searchByTitleAsync = searchByTitleAsync;
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
                var search = await _searchByTitleAsync(
                    source.Title,
                    1,
                    50,
                    targetRegion,
                    null).ConfigureAwait(false);

                var exactMatches = search?.Results?
                    .Where(candidate =>
                        !string.IsNullOrWhiteSpace(candidate.Asin) &&
                        string.Equals(candidate.SkuGroup, source.SkuGroup, StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? new List<AudibleSearchResult>();

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
