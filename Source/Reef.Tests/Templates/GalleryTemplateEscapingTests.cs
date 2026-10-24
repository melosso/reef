using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Reef.Core.TemplateEngines;
using Xunit;

namespace Reef.Tests.Templates;

public partial class GalleryTemplateEscapingTests
{
    private static readonly string TemplatesDir = ScriptTemplateExamplesTests.FindTemplatesDir();

    public static TheoryData<string> MarkupTemplates()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(TemplatesDir, "*Template*.*").Where(p => p.EndsWith(".html") || p.EndsWith(".xml")).Order())
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(MarkupTemplates))]
    public void EveryOutputIsEscapedOrFormatted(string fileName)
    {
        var template = File.ReadAllText(Path.Combine(TemplatesDir, fileName));

        var unescaped = Expression().Matches(template)
            .Select(m => m.Groups["body"].Value.Trim())
            .Where(IsOutput)
            .Where(body => !Regex.IsMatch(body, @"html\.escape|math\.format|date\.to_string"))
            .ToList();

        unescaped.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(MarkupTemplates))]
    public async Task EveryTemplateParsesAndRenders(string fileName)
    {
        var engine = new ScribanTemplateEngine(new ConfigurationBuilder().Build());
        var template = File.ReadAllText(Path.Combine(TemplatesDir, fileName));

        engine.ValidateTemplate(template).IsValid.Should().BeTrue();
        await FluentActions.Awaiting(() => engine.TransformAsync([new() { ["name"] = "A & B" }], template)).Should().NotThrowAsync();
    }

    public static TheoryData<string> XmlTemplates()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(TemplatesDir, "*Template*.xml").Order())
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(XmlTemplates))]
    public async Task XmlTemplatesStayWellFormedWithSpecialCharacters(string fileName)
    {
        const string hostile = "A & B <\"C\">";
        var engine = new ScribanTemplateEngine(new ConfigurationBuilder().Build());
        var template = File.ReadAllText(Path.Combine(TemplatesDir, fileName));
        var formatted = Regex.Matches(template, @"\{\{[^}]*?row\.(\w+)[^}]*?(math\.format|date\.to_string)").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(template, @"parse_json\(row\.(\w+)|row\.(\w+)\s*\|\s*parse_json").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)).ToHashSet();
        var row = Regex.Matches(template, @"row\.(\w+)").Select(m => m.Groups[1].Value)
            .Where(field => !formatted.Contains(field)).Distinct()
            .ToDictionary(field => field, _ => (object)hostile);

        var xml = await engine.TransformAsync([row], template);

        FluentActions.Invoking(() => XDocument.Parse(xml)).Should().NotThrow();
    }

    private static bool IsOutput(string body) =>
        body.Length > 0
        && !body.StartsWith('!')
        && !Regex.IsMatch(body, @"^(for|end|if|else|elsif|while|capture|func|ret|case|when|break|continue)\b")
        && !Regex.IsMatch(body, @"^[\w.]+\s*=[^=]");

    [GeneratedRegex(@"\{\{-?~?(?<body>.*?)~?-?\}\}", RegexOptions.Singleline)]
    private static partial Regex Expression();
}
