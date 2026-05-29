using System.Collections.Generic;

namespace Kavita.Services;

public class GdsFile
{
    public string? Cover { get; set; }
    public int Page { get; set; }
    public int WordCount { get; set; }
    public IDictionary<string, string>? Meta { get; set; }
}
