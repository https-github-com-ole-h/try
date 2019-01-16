﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using static MLS.Protocol.Execution.Workspace;

namespace MLS.Project.Extensions
{

    public static class SourceTextExtensions
    {
        public static IEnumerable<Buffer> ExtractBuffers(this SourceText code, string fileName)
        {
            List<(SyntaxTrivia startRegion, SyntaxTrivia endRegion, string label)> FindRegions(SyntaxNode syntaxNode)
            {
                var nodesWithRegionDirectives =
                    from node in syntaxNode.DescendantNodesAndTokens()
                    where node.HasLeadingTrivia
                    from leadingTrivia in node.GetLeadingTrivia()
                    where leadingTrivia.Kind() == SyntaxKind.RegionDirectiveTrivia ||
                          leadingTrivia.Kind() == SyntaxKind.EndRegionDirectiveTrivia
                    select node;

                var projections = new List<(SyntaxTrivia startRegion, SyntaxTrivia endRegion, string label)>();
                var stack = new Stack<SyntaxTrivia>();
                var processedSpans = new HashSet<TextSpan>();
                foreach (var nodeWithRegionDirective in nodesWithRegionDirectives)
                {
                    var triviaList = nodeWithRegionDirective.GetLeadingTrivia();

                    foreach (var currentTrivia in triviaList)
                    {
                        if (!processedSpans.Add(currentTrivia.FullSpan)) continue;

                        if (currentTrivia.Kind() == SyntaxKind.RegionDirectiveTrivia)
                        {
                            stack.Push(currentTrivia);
                        }
                        else if (currentTrivia.Kind() == SyntaxKind.EndRegionDirectiveTrivia)
                        {
                            var start = stack.Pop();
                            var regionName = start.ToFullString().Replace("#region", string.Empty).Trim();
                            var projectionId = $"{fileName}@{regionName}";
                            projections.Add(
                                (start, currentTrivia, projectionId));
                        }
                    }
                }

                return projections;
            }

            var sourceCodeText = code.ToString();
            var root = CSharpSyntaxTree.ParseText(sourceCodeText).GetRoot();
            var extractedProjections = new List<Buffer>();
            foreach (var (startRegion, endRegion, label) in FindRegions(root))
            {
                var start = startRegion.GetLocation().SourceSpan.End;
                var length = endRegion.GetLocation().SourceSpan.Start -
                             startRegion.GetLocation().SourceSpan.End;
                var loc = new TextSpan(start, length);

                var content = code.ToString(loc);

                content = FormatSourceCode(content);
                extractedProjections.Add(new Buffer(label, content));
            }

            return extractedProjections;
        }

        private static string FormatSourceCode(string sourceCode)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode.Trim(), new CSharpParseOptions(kind: SourceCodeKind.Script));
            var cw = new AdhocWorkspace();
            var formattedCode = Formatter.Format(tree.GetRoot(), cw);
            return formattedCode.ToFullString();
        }
    }
}
