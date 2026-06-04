namespace DockPetWin.Core.Agents;

internal sealed class ActiveSkillContext
{
    public string Name { get; init; } = "";

    public string RootPath { get; init; } = "";

    public string SkillFile { get; init; } = "";

    public string SkillMarkdown { get; init; } = "";

    public List<string> RequiredFiles { get; init; } = [];

    public List<string> SuggestedFiles { get; init; } = [];

    public List<string> StartupFiles { get; init; } = [];

    public List<string> FinalReviewFiles { get; init; } = [];

    public bool RequiresIndexSelectedFile { get; init; }

    public string IndexSelectionReason { get; init; } = "";

    public HashSet<string> ReadFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ReadFileSteps { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int LastNonFinalSkillFileReadStep { get; private set; }

    public void MarkFileRead(string relativePath, int step)
    {
        var normalized = NormalizeSkillPath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        ReadFiles.Add(normalized);
        ReadFileSteps[normalized] = step;
        if (!ContainsPath(FinalReviewFiles, normalized))
        {
            LastNonFinalSkillFileReadStep = Math.Max(LastNonFinalSkillFileReadStep, step);
        }
    }

    public IReadOnlyList<string> GetMissingStartupFiles()
    {
        return StartupFiles
            .Where(path => !ReadFiles.Contains(NormalizeSkillPath(path)))
            .ToList();
    }

    public IReadOnlyList<string> GetMissingRequiredFiles()
    {
        return RequiredFiles
            .Where(path => !ReadFiles.Contains(NormalizeSkillPath(path)))
            .ToList();
    }

    public bool HasReadIndexSelectedContentFile()
    {
        return ReadFiles.Any(path =>
            !ContainsPath(StartupFiles, path)
            && !ContainsPath(FinalReviewFiles, path)
            && !path.Equals("checklist.md", StringComparison.OrdinalIgnoreCase)
            && !path.Equals("README.md", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> GetMissingOrStaleFinalReviewFiles()
    {
        return FinalReviewFiles
            .Where(path =>
            {
                var normalized = NormalizeSkillPath(path);
                return !ReadFileSteps.TryGetValue(normalized, out var readStep)
                    || readStep < LastNonFinalSkillFileReadStep;
            })
            .ToList();
    }

    private static bool ContainsPath(IEnumerable<string> paths, string value)
    {
        var normalized = NormalizeSkillPath(value);
        return paths.Any(path => string.Equals(NormalizeSkillPath(path), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeSkillPath(string value)
    {
        return (value ?? "").Trim().Trim('"', '`').Replace('\\', '/');
    }
}
