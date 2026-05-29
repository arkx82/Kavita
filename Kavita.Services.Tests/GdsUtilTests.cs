using System;
using System.IO;
using Kavita.Services;

namespace Kavita.Services.Tests;

public class GdsUtilTests
{
    [Fact]
    public void GetGdsInfo_ShouldParseWordcountAlias()
    {
        var filePath = WriteYaml("""
                               files:
                                 chapter01.txt:
                                   page: 12
                                   wordcount: 3456
                                   extra: ignored
                               """);

        try
        {
            var info = GdsUtil.GetGdsInfo(filePath);

            Assert.NotNull(info);
            var file = Assert.Single(info.Files!);
            Assert.Equal("chapter01.txt", file.Key);
            Assert.Equal(12, file.Value.Page);
            Assert.Equal(3456, file.Value.WordCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void GetGdsInfo_ShouldParseUnderscoredWordCount()
    {
        var filePath = WriteYaml("""
                               files:
                                 chapter02.txt:
                                   page: 8
                                   word_count: 7890
                               """);

        try
        {
            var info = GdsUtil.GetGdsInfo(filePath);

            Assert.NotNull(info);
            var file = Assert.Single(info.Files!);
            Assert.Equal("chapter02.txt", file.Key);
            Assert.Equal(8, file.Value.Page);
            Assert.Equal(7890, file.Value.WordCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void GetGdsInfo_ShouldParseSeriesMetadata()
    {
        var filePath = WriteYaml("""
                               action:
                                 all_file_is_special: false
                                 first_cover: false
                               files:
                                 000-086 完.epub:
                                   page: 88
                                   wordcount: 396142
                               meta:
                                 Age Rating: '3'
                                 Genres: '판타지 웹소설,동양풍 로판,성인'
                                 Language: ko
                                 Name: 후궁비사
                                 Person Publisher: '메피스토'
                                 Person Writers: '강희자매'
                                 Publication Status: '2'
                                 Summary: '샘플 줄거리'
                                 Tags: '평점9점이상,웹소설,연재완결'
                                 Web Links: 'https://series.naver.com/novel/detail.series?productNo=11727625'
                                 Year: '2024'
                               """);

        try
        {
            var info = GdsUtil.GetGdsInfo(filePath);

            Assert.NotNull(info);
            Assert.NotNull(info.Action);
            Assert.NotNull(info.Meta);
            Assert.Equal("false", info.Action["all_file_is_special"]);
            Assert.Equal("후궁비사", info.Meta["Name"]);
            Assert.Equal("강희자매", info.Meta["Person Writers"]);
            Assert.Equal("메피스토", info.Meta["Person Publisher"]);
            Assert.Equal("샘플 줄거리", info.Meta["Summary"]);
            Assert.Equal("2024", info.Meta["Year"]);

            var file = Assert.Single(info.Files!);
            Assert.Equal("000-086 完.epub", file.Key);
            Assert.Equal(88, file.Value.Page);
            Assert.Equal(396142, file.Value.WordCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string WriteYaml(string contents)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"kavita-gds-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(filePath, contents);

        return filePath;
    }
}
