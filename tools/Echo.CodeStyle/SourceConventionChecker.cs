using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Echo.CodeStyle;

[Export, PartCreationPolicy(CreationPolicy.Shared)]
public sealed class SourceConventionChecker
{
    public int Check(string repository)
    {
        var failures = new List<string>();
        foreach (var folder in new[] { "src", "tests", "tools" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(repository, folder), "*.cs", SearchOption.AllDirectories))
            {
                var generatedFile = path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
                if (generatedFile)
                {
                    continue;
                }

                CheckFile(path, failures);
            }
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        Console.WriteLine($"Source conventions: {failures.Count} violation(s).");
        return failures.Count == 0 ? 0 : 1;
    }

    private void CheckFile(string path, List<string> failures)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToArray();
        if (types.Length > 1 || types.Any(type => type.Identifier.ValueText != Path.GetFileNameWithoutExtension(path)))
        {
            failures.Add($"{path}: put one named type in its matching file.");
        }

        foreach (var type in types.OfType<ClassDeclarationSyntax>())
        {
            if (type.ParameterList != null)
            {
                failures.Add($"{path}: use an explicit constructor.");
            }
        }

        foreach (var token in root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.StaticKeyword)))
        {
            var containingClass = token.Parent?.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            var statelessHelper = containingClass != null
                && containingClass.Modifiers.Any(SyntaxKind.StaticKeyword)
                && containingClass.AttributeLists.Count == 0
                && containingClass.Members.All(member => member is MethodDeclarationSyntax);
            if (token.Parent is not OperatorDeclarationSyntax && !statelessHelper)
            {
                failures.Add($"{path}: reserve statics for stateless algorithm helpers; compose services instead.");
            }
        }

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (field.Modifiers.Any(SyntaxKind.ConstKeyword))
            {
                continue;
            }

            foreach (var variable in field.Declaration.Variables)
            {
                var name = variable.Identifier.ValueText;
                if (!char.IsLower(name[0]))
                {
                    failures.Add($"{path}: field '{name}' must use lowerCamelCase without a prefix.");
                }
            }
        }
    }
}
