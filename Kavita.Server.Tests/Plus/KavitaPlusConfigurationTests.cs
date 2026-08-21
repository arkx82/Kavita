using Kavita.API.Services.Plus;
using Kavita.Models.Entities.Enums;

namespace Kavita.Server.Tests.Plus;

public class KavitaPlusConfigurationTests
{
    #region IsPlusEligible Tests

    [Theory]
    [InlineData(LibraryType.Manga)]
    [InlineData(LibraryType.Comic)]
    [InlineData(LibraryType.Book)]
    [InlineData(LibraryType.Image)]
    [InlineData(LibraryType.LightNovel)]
    [InlineData(LibraryType.ComicVine)]
    public void IsPlusEligible_ReturnsTrue_ForKavitaPlusTypes(LibraryType type)
    {
        Assert.True(KavitaPlusConfiguration.IsPlusEligible(type));
    }

    [Fact]
    public void IsPlusEligible_ReturnsFalse_ForGds()
    {
        // GDS metadata comes from kavita.yaml, so it never participates in Kavita+. Library
        // create/update must skip provider validation for it rather than reject the request.
        Assert.False(KavitaPlusConfiguration.IsPlusEligible(LibraryType.GDS));
    }

    [Fact]
    public void IsPlusEligible_MatchesExternalMetadataServiceGuard_ForEveryLibraryType()
    {
        // ExternalMetadataService and ScrobblingService gate every Kavita+ flow on this predicate.
        // If the two ever disagree, a non-eligible library type could leak into a paid feature.
        foreach (var type in Enum.GetValues<LibraryType>())
        {
            Assert.Equal(
                KavitaPlusConfiguration.IsPlusEligible(type),
                Kavita.Services.Plus.ExternalMetadataService.IsPlusEligible(type));
        }
    }

    #endregion

    #region IsValidMetadataProviderForLibraryType Tests

    [Theory]
    [InlineData(LibraryType.Manga, MetadataProvider.Mangabaka)]
    [InlineData(LibraryType.Comic, MetadataProvider.ComicBookRoundup)]
    [InlineData(LibraryType.Book, MetadataProvider.Hardcover)]
    public void IsValidMetadataProviderForLibraryType_ReturnsTrue_ForSupportedPairs(LibraryType type, MetadataProvider provider)
    {
        Assert.True(KavitaPlusConfiguration.IsValidMetadataProviderForLibraryType(type, provider));
    }

    [Theory]
    [InlineData(LibraryType.Manga, MetadataProvider.Hardcover)]
    [InlineData(LibraryType.Comic, MetadataProvider.Mangabaka)]
    public void IsValidMetadataProviderForLibraryType_ReturnsFalse_ForUnsupportedPairs(LibraryType type, MetadataProvider provider)
    {
        Assert.False(KavitaPlusConfiguration.IsValidMetadataProviderForLibraryType(type, provider));
    }

    [Theory]
    [InlineData(MetadataProvider.Mangabaka)]
    [InlineData(MetadataProvider.Hardcover)]
    [InlineData(MetadataProvider.ComicBookRoundup)]
    public void IsValidMetadataProviderForLibraryType_ReturnsFalse_ForGds_RegardlessOfProvider(MetadataProvider provider)
    {
        Assert.False(KavitaPlusConfiguration.IsValidMetadataProviderForLibraryType(LibraryType.GDS, provider));
    }

    #endregion
}
