/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Metadata;

public partial class MetadataController
{
    /// <summary>
    /// Discover marketplace-local Audible ASINs that belong to the same exact SKU group.
    /// Target regions must be supplied explicitly so one request never fans out to every marketplace by accident.
    /// </summary>
    [HttpGet("audible/{asin}/regional-variants")]
    [ProducesResponseType(typeof(AudibleCrossRegionResolution), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AudibleCrossRegionResolution>> GetAudibleRegionalVariants(
        string asin,
        [FromQuery] string region = "us",
        [FromQuery] string[]? regions = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(asin))
        {
            return BadRequest("ASIN is required");
        }

        var sourceRegion = AudibleRequestHelper.NormalizeRegion(region);
        if (!AudibleRequestHelper.IsSupportedRegion(sourceRegion))
        {
            return BadRequest($"Unsupported Audible source region: {region}");
        }

        var requestedRegions = (regions ?? Array.Empty<string>())
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(AudibleRequestHelper.NormalizeRegion)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedRegions.Count == 0)
        {
            return BadRequest("At least one target Audible region is required via the 'regions' query parameter");
        }

        var unsupportedRegions = requestedRegions
            .Where(regionValue => !AudibleRequestHelper.IsSupportedRegion(regionValue))
            .ToList();
        if (unsupportedRegions.Count > 0)
        {
            return BadRequest($"Unsupported Audible region(s): {string.Join(", ", unsupportedRegions)}");
        }

        var resolver = new AudibleCrossRegionResolver(_audibleService, _logger);
        var resolution = await resolver.ResolveAsync(
            asin,
            sourceRegion,
            requestedRegions,
            cancellationToken).ConfigureAwait(false);

        if (resolution == null)
        {
            return NotFound($"No Audible metadata found for ASIN: {asin}");
        }

        return Ok(resolution);
    }
}
