using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Kavita.Services;

public class GdsFile
{
    public string? Cover { get; set; }
    public int Page { get; set; }
    public int WordCount { get; set; }

    [YamlMember(Alias = "wordcount", ApplyNamingConventions = false)]
    public int WordCountWithoutSeparator
    {
        get => WordCount;
        set => WordCount = value;
    }

    public IDictionary<string, string>? Meta { get; set; }
}
