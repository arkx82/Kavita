using System;
using System.Collections.Generic;
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

    #region Merge Safety Invariants

    // These pin the GDS <-> Kavita+ boundary. GDS is added as a LibraryType enum member, so when
    // upstream introduces a new registry keyed by LibraryType, git merges it cleanly and GDS
    // silently falls out of it. That is how library create/update started rejecting GDS with
    // invalid-metadata-provider. These tests turn that silent gap into a failing build.

    [Fact]
    public void EveryLibraryType_IsExplicitlyClassified_ForKavitaPlus()
    {
        // Every type must be a deliberate yes/no. A new upstream LibraryType fails here until
        // someone decides which side it belongs on.
        var notPlusEligible = new HashSet<LibraryType> { LibraryType.GDS };

        foreach (var type in Enum.GetValues<LibraryType>())
        {
            Assert.Equal(!notPlusEligible.Contains(type), KavitaPlusConfiguration.IsPlusEligible(type));
        }
    }

    [Fact]
    public void EveryPlusEligibleType_HasAtLeastOneProvider()
    {
        // An empty set would pass IsPlusEligible yet leave the UI dropdown empty and make every
        // provider fail validation - the same broken state GDS was in.
        foreach (var (type, providers) in KavitaPlusConfiguration.MetadataProvidersForLibraryTypes)
        {
            Assert.True(providers.Count > 0, $"{type} is Plus eligible but has no metadata providers");
        }
    }

    [Fact]
    public void EveryScrobbleType_IsAlsoPlusEligible()
    {
        // ScrobblingService gates on IsPlusEligible, which reads the *metadata* registry. A type
        // with scrobble providers but no metadata providers would never actually scrobble.
        foreach (var type in KavitaPlusConfiguration.ScrobbleProvidersForLibraryTypes.Keys)
        {
            Assert.True(KavitaPlusConfiguration.IsPlusEligible(type),
                $"{type} has scrobble providers but is not Kavita+ eligible, so scrobbling can never run");
        }
    }

    [Fact]
    public void Gds_HasNoScrobbleProviders()
    {
        Assert.False(KavitaPlusConfiguration.ScrobbleProvidersForLibraryTypes.ContainsKey(LibraryType.GDS));
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
