// Builds Sapling.Web/Data/careers.json from tools/careers/catalogue.json and the O*NET database.
// Run from anywhere inside the repo:  dotnet run tools/careers/import.cs
// O*NET CSVs are downloaded once into tools/careers/.onet-cache (git-ignored).

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var root = FindRepoRoot(Directory.GetCurrentDirectory());
var toolDir = Path.Combine(root, "tools", "careers");
var catalogue = JsonNode.Parse(File.ReadAllText(Path.Combine(toolDir, "catalogue.json")))!.AsObject();
var version = catalogue["onetVersion"]!.GetValue<string>();
var cache = Path.Combine(toolDir, ".onet-cache", version);
Directory.CreateDirectory(cache);

string[] files = ["occupation_data", "task_statements", "software_skills", "essential_skills", "knowledge",
    "job_zones", "job_zone_reference", "related_occupations", "sample_of_reported_titles", "career_interest_types"];

using (var http = new HttpClient())
{
    foreach (var file in files)
    {
        var path = Path.Combine(cache, file + ".csv");
        if (File.Exists(path))
        {
            continue;
        }

        var url = $"https://www.onetcenter.org/dl_files/database/db_{version.Replace('.', '_')}_csv/{file}.csv";
        Console.WriteLine($"Downloading {url}");
        File.WriteAllBytes(path, await http.GetByteArrayAsync(url));
    }
}

// Bright Outlook comes from O*NET OnLine's export rather than the database download.
var brightPath = Path.Combine(cache, "bright_outlook.csv");
if (!File.Exists(brightPath))
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("SaplingCareerImporter/1.0");
    Console.WriteLine("Downloading O*NET Bright Outlook list");
    File.WriteAllBytes(brightPath, await http.GetByteArrayAsync("https://www.onetonline.org/find/bright?b=0&g=Go&fmt=csv"));
}

List<Dictionary<string, string>> Load(string file) => ReadCsv(Path.Combine(cache, file + ".csv"));

var occupations = Load("occupation_data").ToDictionary(r => r["O*NET-SOC Code"]);
var tasks = Load("task_statements").GroupBy(r => r["O*NET-SOC Code"]).ToDictionary(g => g.Key, g => g.ToList());
var software = Load("software_skills").GroupBy(r => r["O*NET-SOC Code"]).ToDictionary(g => g.Key, g => g.ToList());
var essential = Ratings(Load("essential_skills"));
var knowledge = Ratings(Load("knowledge"));
var zones = Load("job_zones").ToDictionary(r => r["O*NET-SOC Code"], r => int.Parse(r["Job Zone"]));
var zoneNames = Load("job_zone_reference").ToDictionary(r => int.Parse(r["Job Zone"]), r => (Name: r["Name"], Education: r["Education"]));
var related = Load("related_occupations").GroupBy(r => r["O*NET-SOC Code"]).ToDictionary(g => g.Key, g => g.ToList());
var titles = Load("sample_of_reported_titles").GroupBy(r => r["O*NET-SOC Code"]).ToDictionary(g => g.Key, g => g.ToList());
var interests = Load("career_interest_types")
    .Where(r => r["Scale ID"] == "OI")
    .GroupBy(r => r["O*NET-SOC Code"])
    .ToDictionary(g => g.Key, g => g.ToDictionary(r => r["Element Name"], r => Math.Round(Num(r["Data Value"]), 2)));

var outlook = Load("bright_outlook").ToDictionary(
    r => r["Code"],
    r => r["Categories"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

var courses = catalogue["courses"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
var techMap = Map(catalogue["technology"]!);
var skillMap = Map(catalogue["essentialSkills"]!, "Professional");
var knowledgeMap = Map(catalogue["knowledge"]!, "Knowledge");
var effort = catalogue["effortByCategory"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.AsArray().Select(v => v!.GetValue<int>()).ToArray());

var roles = catalogue["roles"]!.AsArray().Select(n => n!.AsObject()).ToList();
var included = roles.Select(r => r["onet"]!.GetValue<string>()).ToHashSet();
var problems = new List<string>();
var unmappedTech = new Dictionary<string, int>();

// O*NET's "In Demand" flag is specific to an occupation; "Hot Technology" is popular across all postings,
// which would ask a chemist for HTML. Only the former is used.
Target[] InDemand(string code) => software.GetValueOrDefault(code, [])
    .Where(t => t["In Demand"] == "Y")
    .Select(t => techMap.GetValueOrDefault(t["Workplace Example"]))
    .OfType<Target>()
    .DistinctBy(t => t.Skill)
    .ToArray();

// When a role lists many technologies, the ones shared across its family are the foundations to learn first.
var familyUse = roles
    .GroupBy(r => r["family"]!.GetValue<string>())
    .ToDictionary(g => g.Key, g => g
        .SelectMany(r => InDemand(r["onet"]!.GetValue<string>()))
        .GroupBy(t => t.Skill)
        .ToDictionary(t => t.Key, t => t.Count()));
var output = new JsonArray();

foreach (var role in roles)
{
    var code = role["onet"]!.GetValue<string>();
    if (!occupations.TryGetValue(code, out var occupation))
    {
        problems.Add($"{code} ({role["title"]}) is not in O*NET {version}");
        continue;
    }

    var requirements = new List<JsonObject>();
    var seen = new HashSet<string>();

    foreach (var t in software.GetValueOrDefault(code, []).Where(t => t["In Demand"] == "Y"))
    {
        if (!techMap.ContainsKey(t["Workplace Example"]))
        {
            unmappedTech[t["Workplace Example"]] = unmappedTech.GetValueOrDefault(t["Workplace Example"]) + 1;
        }
    }

    var family = role["family"]!.GetValue<string>();
    var technologies = InDemand(code)
        .OrderByDescending(t => familyUse[family].GetValueOrDefault(t.Skill))
        .ThenBy(t => t.Skill, StringComparer.Ordinal)
        .ToArray();

    foreach (var target in technologies.Take(8))
    {
        seen.Add(target.Skill);
        requirements.Add(Requirement(target, "Technology", 9,
            $"O*NET lists {target.Skill} as in demand for this occupation: it is frequently named in employer job postings."));
    }

    AddRated(essential.GetValueOrDefault(code, []), skillMap, "Skill", 3);
    AddRated(knowledge.GetValueOrDefault(code, []), knowledgeMap, "Knowledge", 4, minImportance: 3.7);

    void AddRated(List<Rating> ratings, Dictionary<string, Target> map, string kind, int max, double minImportance = 3.5)
    {
        var added = 0;
        foreach (var r in ratings.Where(r => r.Importance >= minImportance).OrderByDescending(r => r.Importance))
        {
            if (added >= max || !map.TryGetValue(r.Element, out var target) || !seen.Add(target.Skill))
            {
                continue;
            }

            requirements.Add(Requirement(target, kind, (int)Math.Round(r.Importance * 2),
                $"O*NET rates {r.Element} {r.Importance.ToString("0.0", CultureInfo.InvariantCulture)}/5 for importance in this occupation, at level {r.Level.ToString("0.0", CultureInfo.InvariantCulture)}/7."));
            added++;
        }
    }

    JsonObject Requirement(Target target, string kind, int impact, string rationale)
    {
        var (effortScore, weeks) = effort.TryGetValue(target.Category ?? "", out var e) ? (e[0], e[1]) : (4, 4);
        var node = new JsonObject
        {
            ["skill"] = target.Skill,
            ["kind"] = kind,
            ["impact"] = Math.Clamp(impact, 1, 10),
            ["effort"] = effortScore,
            ["weeks"] = weeks,
            ["rationale"] = rationale,
        };
        if (target.Category is not null)
        {
            node["category"] = target.Category;
        }

        return node;
    }

    var zone = zones.GetValueOrDefault(code);
    var roleTasks = tasks.GetValueOrDefault(code, [])
        .Where(t => t["Task Type"] == "Core")
        .OrderByDescending(t => Num(t["Incumbents Responding"]))
        .Take(5)
        .Select(t => (JsonNode)t["Task"]);

    var relatedCodes = related.GetValueOrDefault(code, [])
        .Where(r => included.Contains(r["Related O*NET-SOC Code"]))
        .OrderBy(r => int.Parse(r["Index"]))
        .Take(4)
        .Select(r => (JsonNode)r["Related O*NET-SOC Code"]);

    var alsoCalled = titles.GetValueOrDefault(code, [])
        .Where(t => t["Shown in My Next Move"] == "Y")
        .Select(t => t["Reported Job Title"])
        .Where(t => !string.Equals(t, role["title"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
        .Take(4)
        .Select(t => (JsonNode)t);

    if (requirements.Count < 3)
    {
        problems.Add($"{code} ({role["title"]}) has only {requirements.Count} mapped requirements");
    }

    output.Add((JsonNode)new JsonObject
    {
        ["onet"] = code,
        ["title"] = role["title"]!.GetValue<string>(),
        ["onetTitle"] = occupation["Title"],
        ["family"] = family,
        ["technologies"] = new JsonArray(technologies.Select(t => (JsonNode)t.Skill).ToArray()),
        ["description"] = occupation["Description"],
        ["tasks"] = new JsonArray(roleTasks.ToArray()),
        ["alsoCalled"] = new JsonArray(alsoCalled.ToArray()),
        ["jobZone"] = zone,
        ["outlook"] = new JsonArray(outlook.GetValueOrDefault(code, []).Select(c => (JsonNode)c).ToArray()),
        ["preparation"] = zoneNames.TryGetValue(zone, out var z) ? z.Name : "",
        ["education"] = zoneNames.TryGetValue(zone, out var z2) ? z2.Education : "",
        ["interests"] = new JsonObject(interests.GetValueOrDefault(code, []).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        ["related"] = new JsonArray(relatedCodes.ToArray()),
        ["core"] = Courses(role["core"]),
        ["adjacent"] = Courses(role["adjacent"]),
        ["primary"] = Courses(role["primary"] ?? new JsonArray()),
        ["requirements"] = new JsonArray(requirements.Cast<JsonNode>().ToArray()),
    });
}

JsonArray Courses(JsonNode? keys) => new(keys!.AsArray().Select(k =>
{
    var key = k!.GetValue<string>();
    if (!courses.TryGetValue(key, out var value))
    {
        problems.Add($"Unknown course key {key}");
    }

    return (JsonNode)(value ?? key);
}).ToArray());

// NPTEL courses for the skills in catalogue.json's "nptel" map. Details and lecture lists come from the same
// public API nptel.ac.in's own course pages use; responses are cached so re-runs do not hit their servers.
var nptelCache = Path.Combine(toolDir, ".onet-cache", "nptel");
Directory.CreateDirectory(nptelCache);
var nptelMap = catalogue["nptel"]!.AsObject();
var nptelCourses = new JsonArray();
using (var nptel = new HttpClient())
{
    nptel.DefaultRequestHeaders.UserAgent.ParseAdd("SaplingCareerImporter/1.0");

    async Task<JsonNode?> FetchAsync(string kind, string id)
    {
        var path = Path.Combine(nptelCache, $"{id}-{kind}.json");
        if (!File.Exists(path))
        {
            await Task.Delay(750);
            Console.WriteLine($"Fetching NPTEL {kind} for {id}");
            var response = await nptel.GetAsync($"https://nptel.ac.in/api/{kind}/{id}");
            if (!response.IsSuccessStatusCode)
            {
                problems.Add($"NPTEL {kind} for {id} returned {(int)response.StatusCode}");
                return null;
            }

            File.WriteAllText(path, await response.Content.ReadAsStringAsync());
        }

        return JsonNode.Parse(File.ReadAllText(path))?["data"];
    }

    foreach (var id in nptelMap.Select(p => p.Value!["course"]!.GetValue<string>()).Distinct())
    {
        var details = await FetchAsync("subject-details", id);
        var downloads = await FetchAsync("downloads", id);
        var lectures = downloads?["course_downloads"]?.AsArray()
            .Select(l => l!["title"]!.GetValue<string>().Trim())
            .Where(t => t.Length > 0)
            .ToList() ?? [];

        if (details is null || lectures.Count == 0)
        {
            problems.Add($"NPTEL course {id} has no details or lecture list");
            continue;
        }

        // NPTEL lectures run about half an hour.
        nptelCourses.Add((JsonNode)new JsonObject
        {
            ["id"] = id,
            ["provider"] = "NPTEL",
            ["title"] = Tidy(details["title"]!.GetValue<string>()),
            ["byline"] = $"{Tidy(details["professor"]?.GetValue<string>() ?? "")}, {Tidy(details["institutename"]?.GetValue<string>() ?? "")}",
            ["url"] = $"https://nptel.ac.in/courses/{id}",
            ["lessons"] = lectures.Count,
            ["minutes"] = lectures.Count * 30,
            ["checkpoints"] = new JsonArray(Checkpoints(lectures).Select(c => (JsonNode)new JsonObject
            {
                ["title"] = c.Title,
                ["lessons"] = c.Lectures,
                ["minutes"] = c.Lectures * 30,
            }).ToArray()),
        });
    }
}

// Microsoft Learn learning paths, from its public catalogue API; each module in a path is a checkpoint.
var msMap = catalogue["microsoftLearn"]!.AsObject();
var msPath = Path.Combine(toolDir, ".onet-cache", "microsoft-learn-catalog.json");
if (!File.Exists(msPath))
{
    using var ms = new HttpClient();
    Console.WriteLine("Downloading the Microsoft Learn catalogue");
    File.WriteAllText(msPath, await ms.GetStringAsync("https://learn.microsoft.com/api/catalog/?locale=en-us&type=learningPaths,modules"));
}

var msCatalog = JsonNode.Parse(File.ReadAllText(msPath))!;
var msModules = msCatalog["modules"]!.AsArray().ToDictionary(m => m!["uid"]!.GetValue<string>(), m => m!);
var msPaths = msCatalog["learningPaths"]!.AsArray().ToDictionary(m => m!["uid"]!.GetValue<string>(), m => m!);
var learnCourses = new JsonArray();
foreach (var uid in msMap.Select(p => p.Value!["path"]!.GetValue<string>()).Distinct())
{
    if (!msPaths.TryGetValue(uid, out var path))
    {
        problems.Add($"Microsoft Learn path {uid} is not in the catalogue");
        continue;
    }

    var modules = path["modules"]!.AsArray().Select(m => m!.GetValue<string>()).Where(msModules.ContainsKey).Select(m => msModules[m]).ToList();
    learnCourses.Add((JsonNode)new JsonObject
    {
        ["id"] = uid,
        ["provider"] = "Microsoft Learn",
        ["title"] = path["title"]!.GetValue<string>(),
        ["byline"] = "Microsoft",
        ["url"] = path["url"]!.GetValue<string>().Split('?')[0],
        ["lessons"] = modules.Count,
        ["minutes"] = path["duration_in_minutes"]!.GetValue<int>(),
        ["checkpoints"] = new JsonArray(modules.Select(m => (JsonNode)new JsonObject
        {
            ["title"] = m["title"]!.GetValue<string>(),
            ["lessons"] = m["units"]?.AsArray().Count ?? 1,
            ["minutes"] = m["duration_in_minutes"]?.GetValue<int>() ?? 0,
        }).ToArray()),
    });
}

var courseIds = nptelCourses.Concat(learnCourses).Select(c => c!["id"]!.GetValue<string>()).ToHashSet();
var skillCourses = new JsonObject(nptelMap.Select(p => (Skill: p.Key, Course: p.Value!["course"]!.GetValue<string>(), Match: p.Value!["match"]!.GetValue<string>()))
    .Concat(msMap.Select(p => (Skill: p.Key, Course: p.Value!["path"]!.GetValue<string>(), Match: p.Value!["match"]!.GetValue<string>())))
    .Where(x => courseIds.Contains(x.Course))
    .GroupBy(x => x.Skill)
    .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)new JsonArray(g.Select(x => (JsonNode)new JsonObject
    {
        ["course"] = x.Course,
        ["match"] = x.Match,
    }).ToArray()))));
var requiredSkills = output
    .SelectMany(r => r!["requirements"]!.AsArray().Select(q => q!["skill"]!.GetValue<string>()))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
foreach (var skill in nptelMap.Concat(msMap).Select(p => p.Key).Where(k => !requiredSkills.Contains(k)))
{
    problems.Add($"NPTEL map lists {skill}, which no role requires");
}

var document = new JsonObject
{
    ["source"] = new JsonObject
    {
        ["name"] = $"O*NET {version} Database",
        ["publisher"] = "U.S. Department of Labor, Employment and Training Administration",
        ["url"] = "https://www.onetcenter.org/database.html",
        ["license"] = "CC BY 4.0",
        ["licenseUrl"] = "https://creativecommons.org/licenses/by/4.0/",
        ["outlookSource"] = "O*NET Bright Outlook, based on U.S. Bureau of Labor Statistics employment projections",
        ["modifications"] = "Selected occupations renamed for Indian students; skill names mapped to Sapling's catalogue; importance and level ratings rescaled to 0-100.",
        ["importedOn"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
    },
    ["courseMapping"] = "Which courses and branches lead to each role is Sapling's own editorial judgement, not O*NET data.",
    ["knowledgeEvidence"] = catalogue["knowledgeEvidence"]!.DeepClone(),
    ["effortNote"] = "Sapling's own rough estimate of effort (1-10) and weeks to close a gap, by skill category. Not O*NET data.",
    ["effortByCategory"] = catalogue["effortByCategory"]!.DeepClone(),
    ["vagueKnowledge"] = catalogue["vagueKnowledge"]!.DeepClone(),
    ["courseBasics"] = new JsonObject(catalogue["courseBasics"]!.AsObject().Select(p =>
        KeyValuePair.Create(courses[p.Key], (JsonNode?)p.Value!.DeepClone()))),
    ["everydaySkills"] = catalogue["everydaySkills"]!.DeepClone(),
    ["learningSource"] = "NPTEL (IITs and IISc, funded by the Ministry of Education) and Microsoft Learn",
    ["learningCourses"] = new JsonArray(nptelCourses.Concat(learnCourses).Select(c => c!.DeepClone()).ToArray()),
    ["skillCourses"] = skillCourses,
    ["roles"] = output,
};

var outPath = Path.Combine(root, "Sapling.Web", "Data", "careers.json");
File.WriteAllText(outPath, document.ToJsonString(new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}) + "\n");

Console.WriteLine($"Wrote {output.Count} roles to {Path.GetRelativePath(root, outPath)}");
foreach (var p in problems)
{
    Console.WriteLine($"WARNING {p}");
}

if (unmappedTech.Count > 0)
{
    Console.WriteLine("In-demand or hot technologies with no mapping in catalogue.json (add one to include them):");
    foreach (var (name, count) in unmappedTech.OrderByDescending(p => p.Value).Take(40))
    {
        Console.WriteLine($"  {count,3}  {name}");
    }
}

return problems.Count == 0 ? 0 : 1;

static Dictionary<string, List<Rating>> Ratings(List<Dictionary<string, string>> rows) => rows
    .Where(r => r["Recommend Suppress"] != "Y")
    .GroupBy(r => (Code: r["O*NET-SOC Code"], Element: r["Element Name"]))
    .Select(g => new
    {
        g.Key.Code,
        Rating = new Rating(
            g.Key.Element,
            Num(g.FirstOrDefault(r => r["Scale ID"] == "IM")?["Data Value"] ?? "0"),
            Num(g.FirstOrDefault(r => r["Scale ID"] == "LV")?["Data Value"] ?? "0")),
    })
    .GroupBy(x => x.Code)
    .ToDictionary(g => g.Key, g => g.Select(x => x.Rating).ToList());

static Dictionary<string, Target> Map(JsonNode node, string? defaultCategory = null) => node.AsObject().ToDictionary(
    p => p.Key,
    p =>
    {
        var parts = p.Value!.GetValue<string>().Split('|');
        return new Target(parts[0], parts.Length > 1 ? parts[1] : defaultCategory);
    });

static double Num(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

static string FindRepoRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Sapling.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run this from inside the Sapling repository.");
}

// Roughly a week of study per checkpoint: by the course's own "Week N"/"Module N" labels when it has them,
// otherwise in even blocks, never more than twelve.
static string Tidy(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

static List<(string Title, int Lectures)> Checkpoints(List<string> lectures)
{
    string Clean(string title) => System.Text.RegularExpressions.Regex.Replace(
        title, @"^\s*(lecture|lec|l)\s*[-.:]?\s*\d+\s*[-.:)]*\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    var labelled = lectures
        .Select(t => System.Text.RegularExpressions.Regex.Match(t, @"^\s*(week|module|unit)\s*[-:]?\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        .ToList();
    if (labelled.All(m => m.Success))
    {
        var groups = lectures.Select((t, i) => (Title: t, Key: $"{labelled[i].Groups[1].Value} {labelled[i].Groups[2].Value}"))
            .GroupBy(x => x.Key)
            .ToList();
        if (groups.Count is >= 3 and <= 15)
        {
            return groups.Select(g =>
            {
                var name = System.Text.RegularExpressions.Regex.Replace(g.First().Title, @"\s*\(?lecture\s*\d+\)?\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                var label = char.ToUpperInvariant(g.Key[0]) + g.Key[1..];
                return (name.StartsWith(label, StringComparison.OrdinalIgnoreCase) ? name : $"{label}: {name}", g.Count());
            }).ToList();
        }
    }

    var count = Math.Clamp((int)Math.Round(lectures.Count / 4.0), 3, 12);
    var size = (int)Math.Ceiling(lectures.Count / (double)count);
    return lectures
        .Chunk(size)
        .Select((chunk, i) =>
        {
            var first = i * size + 1;
            var last = first + chunk.Length - 1;
            var topics = Clean(chunk[0]) + (chunk.Length > 1 ? $" to {Clean(chunk[^1])}" : "");
            return ($"Lectures {first}–{last}: {topics}", chunk.Length);
        })
        .ToList();
}

static List<Dictionary<string, string>> ReadCsv(string path)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var field = new StringBuilder();
    var quoted = false;
    var text = File.ReadAllText(path, Encoding.UTF8);

    for (var i = 0; i < text.Length; i++)
    {
        var c = text[i];
        if (quoted)
        {
            if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
            else if (c == '"') { quoted = false; }
            else { field.Append(c); }
        }
        else if (c == '"') { quoted = true; }
        else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
        else if (c == '\n')
        {
            row.Add(field.ToString().TrimEnd('\r'));
            field.Clear();
            rows.Add(row);
            row = [];
        }
        else { field.Append(c); }
    }

    if (field.Length > 0 || row.Count > 0)
    {
        row.Add(field.ToString());
        rows.Add(row);
    }

    var header = rows[0];
    return rows.Skip(1)
        .Where(r => r.Count == header.Count)
        .Select(r => header.Select((h, i) => (h, r[i])).ToDictionary(p => p.h, p => p.Item2))
        .ToList();
}

sealed record Rating(string Element, double Importance, double Level);

sealed record Target(string Skill, string? Category);
