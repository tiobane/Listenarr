/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Parsing
{
    [Trait("Name", "ParseQualityFromTitleTests")]
    [Trait("Category", "Search")]
    public class ParseQualityFromTitleTests : BaseTests
    {
        [Theory]
        [InlineData("David Baldacci - Zero Day NMR 32 kbps", "32kbps")]
        [InlineData("Book.Title.320kbps", "320kbps")]
        [InlineData("Book Title 192 kbit/s", "192kbps")]
        [InlineData("Book Title 64 kb/s", "64kbps")]
        public void DetectQualityFromTitle_RecognizesExplicitCommonBitrates(string input, string expected)
        {
            Assert.Equal(expected, SearchResultAttributeParser.DetectQualityFromTitle(input));
        }

        [Theory]
        [InlineData("Book Title 32")]
        [InlineData("Book Title 123 kbps")]
        [InlineData("Book Title 2026 64")]
        [InlineData("")]
        public void DetectQualityFromTitle_RejectsAmbiguousOrUnrecognizedNumbers(string input)
        {
            Assert.Null(SearchResultAttributeParser.DetectQualityFromTitle(input));
        }
    }
}
