using System.Collections.Generic;

namespace Kavita.Services;

public class GdsInfo
{
    public IDictionary<string, string>? Action { get; set; }
    public IDictionary<string, GdsFile>? Files { get; set; }
    public IDictionary<string, string>? Meta { get; set; }
    public List<IDictionary<string, string>>? Search { get; set; }
}
