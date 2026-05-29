using System;
using System.IO;
using System.Text;
using Kavita.Models.Entities;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kavita.Services;

public static class GdsUtil
{

    public static GdsInfo? GetGdsInfo(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            return deserializer.Deserialize<GdsInfo>(string.Join("\n", lines));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse GDS yaml at {FilePath}", filePath);
            return null;
        }
    }

    public static GdsInfo? GetGdsInfoBySeries(Series series)
    {
        if (string.IsNullOrEmpty(series.FolderPath)) return null;
        return GetGdsInfo(Path.Join(series.FolderPath, "kavita.yaml"));
    }

    public static GdsInfo? GetGdsInfoByFile(string filePath)
    {
        var directoryName = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directoryName)) return null;
        return GetGdsInfo(Path.Join(directoryName, "kavita.yaml"));
    }

    public static bool SaveCover(string filepath, string cover)
    {
        if (string.IsNullOrEmpty(cover))
        {
            return false;
        }

        try
        {
            File.WriteAllBytes(filepath, Convert.FromBase64String(cover));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static GdsFile? GetGdsFile(GdsInfo? gdsInfo, string key)
    {
        if (gdsInfo?.Files == null || gdsInfo.Files.Count == 0)
        {
            return null;
        }

        if (gdsInfo.Files.TryGetValue(key, out var directMatch))
        {
            return directMatch;
        }

        var normalizedKey = key.Normalize(NormalizationForm.FormKD);
        return gdsInfo.Files.TryGetValue(normalizedKey, out var normalizedMatch) ? normalizedMatch : null;
    }
}
