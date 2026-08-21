using System;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Extensions;
using Kavita.Services.Scanner;

namespace Kavita.Services.Tests.Gds;

/// <summary>
/// Merge-safety invariants for the enum members GDS adds on top of upstream.
/// </summary>
/// <remarks>
/// GDS extends LibraryType, MangaFormat and FileTypeGroup. Upstream code that switches or maps over
/// those enums merges cleanly even when it has no case for the GDS members, so these tests exist to
/// turn "GDS silently fell out of a mapping" into a failing build at merge time.
/// </remarks>
public class GdsEnumCoverageTests
{
    #region FileTypeGroup Coverage

    [Fact]
    public void EveryFileTypeGroup_ResolvesToARegex()
    {
        // GetRegex throws on an unmapped group. Running every member here means a new upstream
        // FileTypeGroup (or a dropped GDS one) fails in tests rather than mid-scan.
        foreach (var group in Enum.GetValues<FileTypeGroup>())
        {
            var regex = group.GetRegex();
            Assert.False(string.IsNullOrWhiteSpace(regex), $"{group} has no extension regex");
        }
    }

    [Fact]
    public void TextFileTypeGroup_MapsToTextExtension()
    {
        Assert.Equal(Parser.TextFileExtension, FileTypeGroup.Text.GetRegex());
    }

    #endregion

    #region MangaFormat Coverage

    [Theory]
    [InlineData("book.txt")]
    [InlineData("C:/Data/Series/chapter 1.txt")]
    public void ParseFormat_ReturnsText_ForTextFiles(string path)
    {
        // GDS relies on .txt being a first-class format. If upstream ever narrows
        // SupportedExtensions, text libraries stop scanning and this catches it.
        Assert.Equal(MangaFormat.Text, Parser.ParseFormat(path));
    }

    [Fact]
    public void SupportedExtensions_IncludesTextExtension()
    {
        Assert.Contains(Parser.TextFileExtension, Parser.SupportedExtensions);
    }

    #endregion

    #region LibraryType Coverage

    [Fact]
    public void GdsLibraryType_HasStableEnumValue()
    {
        // Persisted in the database, so the value must not drift when upstream adds a LibraryType.
        Assert.Equal(6, (int) LibraryType.GDS);
    }

    #endregion
}
