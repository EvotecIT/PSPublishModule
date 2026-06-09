using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed class CommentRemovalService
{
    public string Process(CommentRemovalRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var text = request.Content ?? string.Empty;

        // Tokenize and remove comment tokens using the same state machine as the legacy cmdlet implementation.
        var ast = Parser.ParseInput(text, out Token[] tokens, out ParseError[] _);
        var scriptAst = ast as ScriptBlockAst;

        var toRemove = new List<Token>();
        var skipCommentsUntilParam = false;
        var skipCommentsInsideParam = false;
        var paramParenDepth = 0;
        var paramFound = false;
        var signatureBlock = false;
        var scriptParamOffset = scriptAst?.ParamBlock?.Extent.StartOffset ?? -1;

        foreach (var lineGroup in tokens.GroupBy(token => token.Extent.StartLineNumber))
        {
            var lineTokens = lineGroup.ToArray();
            for (var i = 0; i < lineTokens.Length; i++)
            {
                var token = lineTokens[i];
                var extentText = token.Extent.Text;

                if (string.Equals(extentText, "function", StringComparison.OrdinalIgnoreCase))
                {
                    if (!request.RemoveCommentsBeforeParamBlock)
                        skipCommentsUntilParam = true;
                    continue;
                }

                if (string.Equals(extentText, "param", StringComparison.OrdinalIgnoreCase))
                {
                    paramFound = true;
                    skipCommentsUntilParam = false;

                    if (!request.RemoveCommentsInParamBlock)
                        skipCommentsInsideParam = true;

                    continue;
                }

                if (skipCommentsUntilParam)
                    continue;

                if (paramFound && (string.Equals(extentText, "(", StringComparison.Ordinal) || string.Equals(extentText, "@(", StringComparison.Ordinal)))
                {
                    paramParenDepth += 1;
                }
                else if (paramFound && string.Equals(extentText, ")", StringComparison.Ordinal))
                {
                    paramParenDepth -= 1;
                    if (paramParenDepth == 0)
                    {
                        skipCommentsInsideParam = false;
                        paramFound = false;
                    }
                }

                if (skipCommentsInsideParam)
                    continue;

                if (token.Kind != TokenKind.Comment)
                    continue;

                if (!request.RemoveCommentsBeforeParamBlock && scriptParamOffset >= 0 && token.Extent.EndOffset <= scriptParamOffset)
                    continue;

                if (request.DoNotRemoveSignatureBlock)
                {
                    if (string.Equals(token.Text, "# SIG # Begin signature block", StringComparison.OrdinalIgnoreCase))
                    {
                        signatureBlock = true;
                        continue;
                    }

                    if (signatureBlock)
                    {
                        if (string.Equals(token.Text, "# SIG # End signature block", StringComparison.OrdinalIgnoreCase))
                            signatureBlock = false;

                        continue;
                    }
                }

                toRemove.Add(token);
            }
        }

        foreach (var token in toRemove.OrderByDescending(token => token.Extent.StartOffset))
        {
            var startIndex = token.Extent.StartOffset;
            var howManyChars = token.Extent.EndOffset - token.Extent.StartOffset;
            text = text.Remove(startIndex, howManyChars);
        }

        if (request.RemoveEmptyLines)
        {
            text = Regex.Replace(text, @"(?m)^\s*$", string.Empty);
            text = Regex.Replace(text, @"(?:\r?\n|\n|\r)", "\r\n");
        }

        if (request.RemoveAllEmptyLines)
            text = Regex.Replace(text, @"(?m)^\s*$(\r?\n)?", string.Empty);

        if (!string.IsNullOrEmpty(text))
            text = text.Trim();

        return text;
    }
}
