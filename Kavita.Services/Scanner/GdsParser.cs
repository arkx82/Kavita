using System.IO;
using System.Text.RegularExpressions;
using Kavita.API.Services;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Metadata;
using Kavita.Models.Parser;

namespace Kavita.Services.Scanner;

public class GdsParser(IDirectoryService directoryService, IDefaultParser imageParser) : DefaultParser(directoryService)
{
    public override ParserInfo? Parse(string filePath, string rootPath, string libraryRoot, LibraryType type,
        bool enableMetadata = true, ComicInfo? comicInfo = null)
    {
        var fileNameWithoutExtension = directoryService.FileSystem.Path.GetFileNameWithoutExtension(filePath);
        if (type != LibraryType.Image && Parser.IsCoverImage(directoryService.FileSystem.Path.GetFileName(filePath)))
        {
            return null;
        }

        if (Parser.IsImage(filePath))
        {
            return imageParser.Parse(filePath, rootPath, libraryRoot, LibraryType.Image, enableMetadata, comicInfo);
        }

        var parserInfo = new ParserInfo
        {
            Filename = Path.GetFileName(filePath),
            Format = Parser.ParseFormat(filePath),
            Title = Parser.RemoveExtensionIfSupported(fileNameWithoutExtension),
            FullFilePath = Parser.NormalizePath(filePath),
            Series = string.Empty,
            ComicInfo = comicInfo
        };

        var folderName = Path.GetFileName(Path.GetDirectoryName(filePath));
        folderName = Regex.Replace(folderName, "\\[.*?\\]", string.Empty).Trim();
        folderName = Regex.Replace(folderName, "\\s-{1,2}$", string.Empty).Trim();
        folderName = Regex.Replace(folderName, "\\s~{1,2}$", string.Empty).Trim();
        parserInfo.Series = folderName;
        parserInfo.Chapters = Parser.DefaultChapter;
        parserInfo.Volumes = Parser.ParseVolume(fileNameWithoutExtension, type);
        parserInfo.Edition = string.Empty;
        parserInfo.IsSpecial = parserInfo.Volumes == Parser.LooseLeafVolume;

        if (Path.Exists(Path.Join(libraryRoot, ".special")) || Path.Exists(Path.Join(Path.GetDirectoryName(filePath), ".special")))
        {
            parserInfo.IsSpecial = true;
            parserInfo.Volumes = Parser.LooseLeafVolume;
        }

        return parserInfo.Series == string.Empty ? null : parserInfo;
    }

    public override bool IsApplicable(string filePath, LibraryType type)
    {
        return type == LibraryType.GDS;
    }
}
