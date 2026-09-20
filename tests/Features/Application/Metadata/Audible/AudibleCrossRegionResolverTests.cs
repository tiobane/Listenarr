/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Metadata.Audible
{
    [Trait("Name", "AudibleCrossRegionResolverTests")]
    [Trait("Category", "AudibleService")]
    [Trait("Third-Party", "Audible")]
    public sealed class AudibleCrossRegionResolverTests : BaseTests
    {
        [Fact]
        public async Task ResolveAsync_AcceptsOnlyExactSkuGroupMatches()
        {
            var source = new AudibleBookResponse
            {
                Asin = "B00H7CH4Z8",
                Title = "Zero Day",
                Region = "de",
                Sku = "BK_RHDE_002148DE",
                SkuGroup = "BK_RHDE_002148",
                Authors = new List<AudibleAuthor> { new() { Name = "David Baldacci" } }
            };

            var titleSearchCalled = false;
            var resolver = new AudibleCrossRegionResolver(
                (_, _, _, _) => Task.FromResult<AudibleBookResponse?>(source),
                (_, _, _, _, _) =>
                {
                    titleSearchCalled = true;
                    return Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse());
                },
                (_, _, _, _, _, _) => Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse
                {
                    Results = new List<AudibleSearchResult>
                    {
                        new()
                        {
                            Asin = "B00WRONG01",
                            Title = "Zero Day",
                            Sku = "BK_OTHER_000001",
                            SkuGroup = "BK_OTHER_000001"
                        },
                        new()
                        {
                            Asin = "B00TKMPBZ8",
                            Title = "Zero Day",
                            Sku = "BK_RHDE_002148",
                            SkuGroup = "BK_RHDE_002148"
                        }
                    },
                    TotalResults = 2
                }),
                NullLogger.Instance);

            var result = await resolver.ResolveAsync(
                source.Asin!,
                "de",
                new[] { "de", "us" });

            Assert.NotNull(result);
            Assert.False(titleSearchCalled);
            Assert.Equal("BK_RHDE_002148", result.SkuGroup);
            Assert.Equal(2, result.Variants.Count);
            Assert.Contains(result.Variants, variant => variant.Region == "de" && variant.Asin == "B00H7CH4Z8");
            Assert.Contains(result.Variants, variant => variant.Region == "us" && variant.Asin == "B00TKMPBZ8");
            Assert.DoesNotContain(result.Variants, variant => variant.Asin == "B00WRONG01");
        }

        [Fact]
        public async Task ResolveAsync_FallsBackToPagedTitleSearchUntilExactSkuGroupIsFound()
        {
            var source = new AudibleBookResponse
            {
                Asin = "B00H42MTR4",
                Title = "Zero Day",
                Region = "de",
                Sku = "BK_RHDE_002150DE",
                SkuGroup = "BK_RHDE_002150"
            };

            var searchedPages = new List<int>();
            var resolver = new AudibleCrossRegionResolver(
                (_, _, _, _) => Task.FromResult<AudibleBookResponse?>(source),
                (_, page, _, _, _) =>
                {
                    searchedPages.Add(page);
                    if (page == 1)
                    {
                        var wrongResults = Enumerable.Range(1, 50)
                            .Select(index => new AudibleSearchResult
                            {
                                Asin = $"B00WR{index:00000}",
                                Title = "Zero Day",
                                SkuGroup = "BK_RHDE_002148"
                            })
                            .ToList();

                        return Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse
                        {
                            Results = wrongResults,
                            TotalResults = 51
                        });
                    }

                    return Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse
                    {
                        Results = new List<AudibleSearchResult>
                        {
                            new()
                            {
                                Asin = "B00U07JF72",
                                Title = "Zero Day",
                                Sku = "BK_RHDE_002150",
                                SkuGroup = "BK_RHDE_002150"
                            }
                        },
                        TotalResults = 51
                    });
                },
                (_, _, _, _, _, _) => Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse()),
                NullLogger.Instance);

            var result = await resolver.ResolveAsync(source.Asin!, "de", new[] { "us" });

            Assert.NotNull(result);
            Assert.Equal(new[] { 1, 2 }, searchedPages);
            Assert.Contains(result.Variants, variant => variant.Region == "us" && variant.Asin == "B00U07JF72");
            Assert.DoesNotContain(result.Variants, variant => variant.Asin.StartsWith("B00WR", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ResolveAsync_MissingSkuGroup_DoesNotSearchOtherRegions()
        {
            var searchCalled = false;
            var source = new AudibleBookResponse
            {
                Asin = "B00NOSKUGRP",
                Title = "Book Without SKU Group",
                Region = "de",
                Sku = "BK_TEST_000001DE",
                SkuGroup = null
            };

            var resolver = new AudibleCrossRegionResolver(
                (_, _, _, _) => Task.FromResult<AudibleBookResponse?>(source),
                (_, _, _, _, _) =>
                {
                    searchCalled = true;
                    return Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse());
                },
                (_, _, _, _, _, _) =>
                {
                    searchCalled = true;
                    return Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse());
                },
                NullLogger.Instance);

            var result = await resolver.ResolveAsync(
                source.Asin!,
                "de",
                new[] { "us" });

            Assert.NotNull(result);
            Assert.False(searchCalled);
            Assert.Null(result.SkuGroup);
            Assert.Single(result.Variants);
            Assert.Equal("de", result.Variants[0].Region);
            Assert.Equal("B00NOSKUGRP", result.Variants[0].Asin);
        }

        [Fact]
        public async Task ResolveAsync_NormalizesAndDeduplicatesTargetRegions()
        {
            var searchedRegions = new List<string>();
            var source = new AudibleBookResponse
            {
                Asin = "B00H7CH4Z8",
                Title = "Zero Day",
                Region = "de",
                Sku = "BK_RHDE_002148DE",
                SkuGroup = "BK_RHDE_002148"
            };

            var resolver = new AudibleCrossRegionResolver(
                (_, _, _, _) => Task.FromResult<AudibleBookResponse?>(source),
                (_, _, _, region, _) =>
                {
                    searchedRegions.Add(region);
                    return Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse
                    {
                        Results = new List<AudibleSearchResult>(),
                        TotalResults = 0
                    });
                },
                (_, _, _, _, _, _) => Task.FromResult<AudibleSearchResponse?>(new AudibleSearchResponse()),
                NullLogger.Instance);

            await resolver.ResolveAsync(
                source.Asin!,
                "de",
                new[] { "US", "us", "bogus", "DE" });

            Assert.Single(searchedRegions);
            Assert.Equal("us", searchedRegions[0]);
        }
    }
}
